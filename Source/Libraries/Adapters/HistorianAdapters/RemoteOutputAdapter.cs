﻿//******************************************************************************************************
//  RemoteOutputAdapter.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  06/01/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  09/15/2009 - Stephen C. Will
//       Added new header and license agreement.
//  09/22/2009 - Pinal C. Patel
//       Re-wrote the adapter to utilize new components.
//  09/23/2009 - Pinal C. Patel
//       Fixed the handling of socket disconnect.
//  03/04/2010 - Pinal C. Patel
//       Added outputIsForArchive and throttleTransmission setting parameters for more control over 
//       the adapter.
//       Switched to ManualResetEvent for waiting on historian acknowledgement for efficiency.
//  01/20/2011 - Pinal C. Patel
//       Modified to use Settings for the ConnectionString property of historian socket.
//  12/13/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using GSF;
using GSF.Communication;
using GSF.Diagnostics;
using GSF.Historian.Packets;
using GSF.Parsing;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;

namespace HistorianAdapters
{
    /// <summary>
    /// Represents an output adapter that publishes measurements to openHistorian for archival.
    /// </summary>
    [Description("Remote v1.0 Historian Publisher: Forwards measurements to a remote 1.0 openHistorian for archival")]
    public class RemoteOutputAdapter : OutputAdapterBase
    {
        #region [ Members ]

        // Constants
        private const int DefaultHistorianPort = 1003;
        private const bool DefaultPayloadAware = true;
        private const bool DefaultConserveBandwidth = true;
        private const bool DefaultOutputIsForArchive = true;
        private const bool DefaultThrottleTransmission = true;
        private const int DefaultSamplesPerTransmission = 100000;
        private const int PublisherWaitTime = 5000;

        // Fields
        private string m_server;
        private int m_port;
        private bool m_payloadAware;
        private bool m_conserveBandwidth;
        private bool m_outputIsForArchive;
        private bool m_throttleTransmission;
        private int m_samplesPerTransmission;
        private readonly TcpClient m_historianPublisher;
        private byte[] m_publisherBuffer;
        private readonly ManualResetEvent m_publisherWaitHandle;
        private Action<IMeasurement[], int, int> m_publisherDelegate;
        private bool m_publisherDisconnecting;
        private long m_measurementsPublished;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteOutputAdapter"/> class.
        /// </summary>
        public RemoteOutputAdapter()
        {
            m_historianPublisher = new TcpClient();
            m_publisherWaitHandle = new ManualResetEvent(false);

            m_port = DefaultHistorianPort;
            m_payloadAware = DefaultPayloadAware;
            m_conserveBandwidth = DefaultConserveBandwidth;
            m_outputIsForArchive = DefaultOutputIsForArchive;
            m_throttleTransmission = DefaultThrottleTransmission;
            m_samplesPerTransmission = DefaultSamplesPerTransmission;
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the host name for the server hosting the remote historian.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define the host name of the remote historian.")]
        public string Server
        {
            get
            {
                return m_server;
            }
            set
            {
                m_server = value;
            }
        }

        /// <summary>
        /// Gets or sets the port on which the remote historian is listening.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define the port on which the remote historian is listening."),
        DefaultValue(1003)]
        public int Port
        {
            get
            {
                return m_port;
            }
            set
            {
                m_port = value;
            }
        }

        /// <summary>
        /// Gets or sets a boolean value indicating whether the payload
        /// boundaries are to be preserved during transmission.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define a value indicating whether to preserve payload boundaries during transmission."),
        DefaultValue(true)]
        public bool PayloadAware
        {
            get
            {
                return m_payloadAware;
            }
            set
            {
                m_payloadAware = value;
            }
        }

        /// <summary>
        /// Gets or sets a boolean value that determines the packet
        /// type to be used when sending data to the server.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define a value indicating the packet type when sending data to the server."),
        DefaultValue(true)]
        public bool ConserveBandwidth
        {
            get
            {
                return m_conserveBandwidth;
            }
            set
            {
                m_conserveBandwidth = value;
            }
        }

        /// <summary>
        /// Returns a flag that determines if measurements sent to this <see cref="RemoteOutputAdapter"/> are destined for archival.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define a value that determines whether the measurements are destined for archival."),
        DefaultValue(true)]
        public override bool OutputIsForArchive => m_outputIsForArchive;

        /// <summary>
        /// Gets or sets a boolean value that determines whether to wait for
        /// acknowledgment from the historian that the last set of points have
        /// been received before attempting to send the next set of points.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define a value that determines whether to wait for acknowledgment before sending more points."),
        DefaultValue(true)]
        public bool ThrottleTransmission
        {
            get
            {
                return m_throttleTransmission;
            }
            set
            {
                m_throttleTransmission = value;
            }
        }

        /// <summary>
        /// Gets or sets an integer that indicates the maximum number
        /// of points to be published to the historian at once.
        /// </summary>
        [ConnectionStringParameter,
        Description("Define the maximum number of points to be published at once."),
        DefaultValue(100000)]
        public int SamplesPerTransmission
        {
            get
            {
                return m_samplesPerTransmission;
            }
            set
            {
                m_samplesPerTransmission = value;
            }
        }

        /// <summary>
        /// Gets flag that determines if this <see cref="RemoteOutputAdapter"/> uses an asynchronous connection.
        /// </summary>
        protected override bool UseAsyncConnect => true;

        /// <summary>
        /// Returns the detailed status of the data output source.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new StringBuilder();
                status.Append(base.Status);
                status.AppendLine();
                status.Append(m_historianPublisher.Status);

                return status.ToString();
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Initializes this <see cref="RemoteOutputAdapter"/>.
        /// </summary>
        /// <exception cref="ArgumentException"><b>Server</b> is missing from the <see cref="AdapterBase.Settings"/>.</exception>
        public override void Initialize()
        {
            base.Initialize();

            string errorMessage = "{0} is missing from Settings - Example: server=localhost;port=1003;payloadAware=True;conserveBandwidth=True;outputIsForArchive=True;throttleTransmission=True;samplesPerTransmission=100000";
            Dictionary<string, string> settings = Settings;
            string setting;

            // Validate settings.
            if (!settings.TryGetValue("server", out m_server))
                throw new ArgumentException(string.Format(errorMessage, "server"));

            if (settings.TryGetValue("port", out setting))
                m_port = int.Parse(setting);
            else
                settings.Add("port", m_port.ToString());

            if (settings.TryGetValue("payloadaware", out setting))
                m_payloadAware = setting.ParseBoolean();

            if (settings.TryGetValue("conservebandwidth", out setting))
                m_conserveBandwidth = setting.ParseBoolean();

            if (settings.TryGetValue("outputisforarchive", out setting))
                m_outputIsForArchive = setting.ParseBoolean();

            if (settings.TryGetValue("throttletransmission", out setting))
                m_throttleTransmission = setting.ParseBoolean();

            if (settings.TryGetValue("samplespertransmission", out setting))
                m_samplesPerTransmission = int.Parse(setting);

            // Initialize publisher delegates.
            if (m_conserveBandwidth)
            {
                m_publisherDelegate = TransmitPacketType101;
            }
            else
            {
                m_publisherDelegate = TransmitPacketType1;
                m_publisherBuffer = new byte[m_samplesPerTransmission * PacketType1.FixedLength];
            }

            // Initialize publisher socket.
            m_historianPublisher.ConnectionString = settings.JoinKeyValuePairs();
            m_historianPublisher.PayloadAware = m_payloadAware;
            m_historianPublisher.ConnectionAttempt += HistorianPublisher_ConnectionAttempt;
            m_historianPublisher.ConnectionEstablished += HistorianPublisher_ConnectionEstablished;
            m_historianPublisher.ConnectionTerminated += HistorianPublisher_ConnectionTerminated;
            m_historianPublisher.SendDataException += HistorianPublisher_SendDataException;
            m_historianPublisher.ReceiveDataComplete += HistorianPublisher_ReceiveDataComplete;
            m_historianPublisher.ReceiveDataException += HistorianPublisher_ReceiveDataException;
            m_historianPublisher.Initialize();
        }

        /// <summary>
        /// Gets a short one-line status of this <see cref="RemoteOutputAdapter"/>.
        /// </summary>
        /// <param name="maxLength">Maximum length of the status message.</param>
        /// <returns>Text of the status message.</returns>
        public override string GetShortStatus(int maxLength)
        {
            if (m_outputIsForArchive)
                return $"Published {m_measurementsPublished} measurements for archival.".CenterText(maxLength);

            return $"Published {m_measurementsPublished} measurements for processing.".CenterText(maxLength);
        }

        /// <summary>
        /// Releases the unmanaged resources used by this <see cref="RemoteOutputAdapter"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    // This will be done regardless of whether the object is finalized or disposed.

                    if (disposing)
                    {
                        // This will be done only when the object is disposed by calling Dispose().
                        if (m_historianPublisher != null)
                        {
                            m_historianPublisher.ConnectionAttempt -= HistorianPublisher_ConnectionAttempt;
                            m_historianPublisher.ConnectionEstablished -= HistorianPublisher_ConnectionEstablished;
                            m_historianPublisher.ConnectionTerminated -= HistorianPublisher_ConnectionTerminated;
                            m_historianPublisher.SendDataException -= HistorianPublisher_SendDataException;
                            m_historianPublisher.ReceiveDataComplete -= HistorianPublisher_ReceiveDataComplete;
                            m_historianPublisher.ReceiveDataException -= HistorianPublisher_ReceiveDataException;
                            m_historianPublisher.Dispose();
                        }

                        m_publisherWaitHandle.Close();
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Attempts to connect to this <see cref="RemoteOutputAdapter"/>.
        /// </summary>
        protected override void AttemptConnection()
        {
            m_publisherDisconnecting = false;
            m_historianPublisher.ConnectAsync();
        }

        /// <summary>
        /// Attempts to disconnect from this <see cref="RemoteOutputAdapter"/>.
        /// </summary>
        protected override void AttemptDisconnection()
        {
            m_publisherDisconnecting = true;
            m_historianPublisher.Disconnect();
        }

        /// <summary>
        /// Publishes <paramref name="measurements"/> for archival.
        /// </summary>
        /// <param name="measurements">Measurements to be archived.</param>
        /// <exception cref="OperationCanceledException">Acknowledgment is not received from historian for published data.</exception>
        protected override void ProcessMeasurements(IMeasurement[] measurements)
        {
            if (m_historianPublisher.CurrentState != ClientState.Connected)
                throw new InvalidOperationException("Historian publisher socket is not connected");

            try
            {
                for (int i = 0; i < measurements.Length; i += m_samplesPerTransmission)
                {
                    // Wait for historian acknowledgment.
                    if (m_throttleTransmission)
                    {
                        if (!m_publisherWaitHandle.WaitOne(PublisherWaitTime))
                            throw new OperationCanceledException("Timeout waiting for acknowledgment from historian");
                    }

                    // Publish measurements to historian.
                    m_publisherWaitHandle.Reset();
                    m_publisherDelegate(measurements, i, (measurements.Length - i < m_samplesPerTransmission ? measurements.Length : i + m_samplesPerTransmission) - 1);
                }
                m_measurementsPublished += measurements.Length;
            }
            catch
            {
                m_publisherWaitHandle.Set();
                throw;
            }
        }

        private void HistorianPublisher_ConnectionAttempt(object sender, EventArgs e)
        {
            OnStatusMessage(MessageLevel.Info, "Attempting socket connection...");
        }

        private void HistorianPublisher_ConnectionEstablished(object sender, EventArgs e)
        {
            OnConnected();
            m_publisherWaitHandle.Set();
        }

        private void HistorianPublisher_ConnectionTerminated(object sender, EventArgs e)
        {
            m_measurementsPublished = 0;
            m_publisherWaitHandle.Reset();

            if (!m_publisherDisconnecting)
                Start();
        }

        private void HistorianPublisher_SendDataException(object sender, EventArgs<Exception> e)
        {
            m_publisherWaitHandle.Set();
            OnProcessException(MessageLevel.Warning, e.Argument);
        }

        private void HistorianPublisher_ReceiveDataComplete(object sender, EventArgs<byte[], int> e)
        {
            // Check for acknowledgment from historian.
            string reply = Encoding.ASCII.GetString(e.Argument1, 0, e.Argument2);
            if (reply == "ACK")
                m_publisherWaitHandle.Set();
        }

        private void HistorianPublisher_ReceiveDataException(object sender, EventArgs<Exception> e)
        {
            m_publisherWaitHandle.Set();
            OnProcessException(MessageLevel.Warning, e.Argument);
        }

        private void TransmitPacketType1(IMeasurement[] measurements, int startIndex, int endIndex)
        {
            int bufferIndex = 0;

            for (int i = startIndex; i <= endIndex; i++)
                bufferIndex += new PacketType1(measurements[i]).GenerateBinaryImage(m_publisherBuffer, bufferIndex);

            m_historianPublisher.SendAsync(m_publisherBuffer, 0, bufferIndex);
        }

        private void TransmitPacketType101(IMeasurement[] measurements, int startIndex, int endIndex)
        {
            PacketType101 packet = new PacketType101();

            for (int i = startIndex; i <= endIndex; i++)
                packet.Data.Add(new PacketType101DataPoint(measurements[i]));

            m_historianPublisher.SendAsync(packet.BinaryImage());
        }

        #endregion
    }
}
