﻿//******************************************************************************************************
//  SynchronizedClientSubscription.cs - Gbtc
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
//  06/24/2011 - Ritchie
//       Generated original version of source code.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GSF.Diagnostics;
using GSF.IO;
using GSF.Parsing;
using GSF.Threading;
using GSF.TimeSeries.Adapters;
using GSF.TimeSeries.Transport.TSSC;

// ReSharper disable InconsistentlySynchronizedField
// ReSharper disable PossibleMultipleEnumeration
namespace GSF.TimeSeries.Transport
{
    /// <summary>
    /// Represents a synchronized client subscription to the <see cref="DataPublisher" />.
    /// </summary>
    internal class SynchronizedClientSubscription : ActionAdapterBase, IClientSubscription
    {
        #region [ Members ]

        // Events

        /// <summary>
        /// Indicates that a buffer block needed to be retransmitted because
        /// it was previously sent, but no confirmation was received.
        /// </summary>
        public event EventHandler BufferBlockRetransmission;

        /// <summary>
        /// Indicates to the host that processing for an input adapter (via temporal session) has completed.
        /// </summary>
        /// <remarks>
        /// This event is expected to only be raised when an input adapter has been designed to process
        /// a finite amount of data, e.g., reading a historical range of data during temporal processing.
        /// </remarks>
        public event EventHandler<EventArgs<IClientSubscription, EventArgs>> ProcessingComplete;

        // Fields
        private DataPublisher m_parent;
        private volatile byte m_compressionStrength;
        private volatile bool m_usePayloadCompression;
        private volatile bool m_useCompactMeasurementFormat;
        private readonly CompressionModes m_compressionModes;
        private bool m_resetTsscEncoder;
        private readonly object m_tsscSyncLock = new();
        private TsscEncoder m_tsscEncoder;
        private byte[] m_tsscWorkingBuffer;
        private ushort m_tsscSequenceNumber;
        private volatile bool m_startTimeSent;
        private volatile bool m_isNaNFiltered;
        private IaonSession m_iaonSession;

        private readonly List<byte[]> m_bufferBlockCache;
        private readonly object m_bufferBlockCacheLock;
        private uint m_bufferBlockSequenceNumber;
        private uint m_expectedBufferBlockConfirmationNumber;
        private SharedTimer m_bufferBlockRetransmissionTimer;
        private double m_bufferBlockRetransmissionTimeout;

        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        /// <param name="parent">Reference to parent.</param>
        /// <param name="clientID"><see cref="Guid"/> based client connection ID.</param>
        /// <param name="subscriberID"><see cref="Guid"/> based subscriber ID.</param>
        /// <param name="compressionModes"><see cref="CompressionModes"/> requested by client.</param>
        public SynchronizedClientSubscription(DataPublisher parent, Guid clientID, Guid subscriberID, CompressionModes compressionModes)
        {
            m_parent = parent;
            ClientID = clientID;
            SubscriberID = subscriberID;
            m_compressionModes = compressionModes;

            SignalIndexCache = new SignalIndexCache
            {
                SubscriberID = subscriberID
            };

            m_bufferBlockCache = new List<byte[]>();
            m_bufferBlockCacheLock = new object();
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets name of the action adapter.
        /// </summary>
        public override string Name
        {
            get => base.Name;

            set
            {
                base.Name = value;
                Log.InitialStackMessages = Log.InitialStackMessages.Union("AdapterName", GetType().Name, "HostName", value);
            }
        }

        /// <summary>
        /// Gets the <see cref="Guid"/> client TCP connection identifier of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public Guid ClientID { get; }

        /// <summary>
        /// Gets the <see cref="Guid"/> based subscriber ID of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public Guid SubscriberID { get; }

        /// <summary>
        /// Gets the current signal index cache of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public SignalIndexCache SignalIndexCache { get; }

        public string RequestedInputFilter { get; private set; }

        /// <summary>
        /// Gets or sets flag that determines if payload compression should be enabled in data packets of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public bool UsePayloadCompression
        {
            get => m_usePayloadCompression;
            set => m_usePayloadCompression = value;
        }

        /// <summary>
        /// Gets or sets the compression strength value to use when <see cref="UsePayloadCompression"/> is <c>true</c> for this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public int CompressionStrength
        {
            get => m_compressionStrength;
            set
            {
                if (value < 0)
                    value = 0;

                if (value > 31)
                    value = 31;

                m_compressionStrength = (byte)value;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if the compact measurement format should be used in data packets of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public bool UseCompactMeasurementFormat
        {
            get => m_useCompactMeasurementFormat;
            set => m_useCompactMeasurementFormat = value;
        }

        /// <summary>
        /// Gets size of timestamp in bytes.
        /// </summary>
        public int TimestampSize => 8;

        /// <summary>
        /// Gets or sets the desired processing interval, in milliseconds, for the adapter.
        /// </summary>
        /// <remarks>
        /// With the exception of the values of -1 and 0, this value specifies the desired processing interval for data, i.e.,
        /// basically a delay, or timer interval, over which to process data. A value of -1 means to use the default processing
        /// interval while a value of 0 means to process data as fast as possible.
        /// </remarks>
        public override int ProcessingInterval
        {
            get => base.ProcessingInterval;
            set
            {
                base.ProcessingInterval = value;

                // Update processing interval in private temporal session, if defined
                if (m_iaonSession?.AllAdapters != null)
                    m_iaonSession.AllAdapters.ProcessingInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets primary keys of input measurements the <see cref="SynchronizedClientSubscription"/> expects, if any.
        /// </summary>
        /// <remarks>
        /// We override method so assignment can be synchronized such that dynamic updates won't interfere
        /// with filtering in <see cref="QueueMeasurementsForProcessing"/>.
        /// </remarks>
        public override MeasurementKey[] InputMeasurementKeys
        {
            get => base.InputMeasurementKeys;
            set
            {
                lock (this)
                {
                    // Update signal index cache unless "detaching" from real-time
                    if (value is not null && !(value.Length == 1 && value[0] == MeasurementKey.Undefined))
                    {
                        m_parent.UpdateSignalIndexCache(ClientID, SignalIndexCache, value);

                        if (DataSource is not null && SignalIndexCache is not null)
                            value = AdapterBase.ParseInputMeasurementKeys(DataSource, false, string.Join("; ", SignalIndexCache.AuthorizedSignalIDs));
                    }

                    base.InputMeasurementKeys = value;
                }
            }
        }

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        /// <remarks>
        /// Although this adapter provisions support for temporal processing by proxying historical data to a remote sink, the adapter
        /// does not need to be automatically engaged within an actual temporal <see cref="IaonSession"/>, therefore this method returns
        /// <c>false</c> to make sure the adapter doesn't get automatically instantiated within a temporal session.
        /// </remarks>
        public override bool SupportsTemporalProcessing => false;

        /// <summary>
        /// Gets a formatted message describing the status of this <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new();

                if (m_parent.ClientConnections.TryGetValue(ClientID, out ClientConnection connection))
                {
                    status.Append(connection.Status);
                    status.AppendLine();
                }

                status.Append(base.Status);

                if (m_iaonSession is not null)
                    status.Append(m_iaonSession.Status);

                return status.ToString();
            }
        }

        /// <summary>
        /// Gets the status of the active temporal session, if any.
        /// </summary>
        public string TemporalSessionStatus => m_iaonSession?.Status;

        int IClientSubscription.MeasurementReportingInterval { get; set; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="SynchronizedClientSubscription"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (m_disposed)
                return;

            try
            {
                if (!disposing)
                    return;

                // Remove reference to parent
                m_parent = null;

                // Dispose Iaon session
                this.DisposeTemporalSession(ref m_iaonSession);
            }
            finally
            {
                m_disposed = true;          // Prevent duplicate dispose.
                base.Dispose(disposing);    // Call base class Dispose().
            }
        }

        /// <summary>
        /// Initializes <see cref="SynchronizedClientSubscription"/>.
        /// </summary>
        public override void Initialize()
        {
            MeasurementKey[] inputMeasurementKeys;

            if (Settings.TryGetValue(nameof(inputMeasurementKeys), out string setting))
            {
                // IMPORTANT: The allowSelect argument of ParseInputMeasurementKeys must be null
                //            in order to prevent SQL injection via the subscription filter expression
                inputMeasurementKeys = AdapterBase.ParseInputMeasurementKeys(DataSource, false, setting);
                RequestedInputFilter = setting;

                // IMPORTANT: We need to remove the setting before calling base.Initialize()
                //            or else we will still be subject to SQL injection
                Settings.Remove(nameof(inputMeasurementKeys));
            }
            else
            {
                inputMeasurementKeys = Array.Empty<MeasurementKey>();
                RequestedInputFilter = null;
            }

            base.Initialize();

            // Set the InputMeasurementKeys and UsePrecisionTimer properties after calling
            // base.Initialize() so that the base class does not overwrite our settings
            InputMeasurementKeys = inputMeasurementKeys;
            UsePrecisionTimer = false;

            m_bufferBlockRetransmissionTimeout = Settings.TryGetValue("bufferBlockRetransmissionTimeout", out setting) ? double.Parse(setting) : 5.0D;

            if (Settings.TryGetValue("requestNaNValueFilter", out setting))
                m_isNaNFiltered = m_parent.AllowNaNValueFilter && setting.ParseBoolean();
            else
                m_isNaNFiltered = false;

            m_bufferBlockRetransmissionTimer = Common.TimerScheduler.CreateTimer((int)(m_bufferBlockRetransmissionTimeout * 1000.0D));
            m_bufferBlockRetransmissionTimer.AutoReset = false;
            m_bufferBlockRetransmissionTimer.Elapsed += BufferBlockRetransmissionTimer_Elapsed;

            // Handle temporal session initialization
            if (this.TemporalConstraintIsDefined())
                m_iaonSession = this.CreateTemporalSession();
        }

        /// <summary>
        /// Starts the <see cref="SynchronizedClientSubscription"/> or restarts it if it is already running.
        /// </summary>
        public override void Start()
        {
            if (!Enabled)
                m_startTimeSent = false;

            // Reset compressor on successful resubscription
            m_resetTsscEncoder = true;
            m_tsscSequenceNumber = 0;

            base.Start();
        }

        /// <summary>
        /// Queues a collection of measurements for processing.
        /// </summary>
        /// <param name="measurements">Collection of measurements to queue for processing.</param>
        /// <remarks>
        /// Measurements are filtered against the defined <see cref="InputMeasurementKeys"/> so we override method
        /// so that dynamic updates to keys will be synchronized with filtering to prevent interference.
        /// </remarks>
        public override void QueueMeasurementsForProcessing(IEnumerable<IMeasurement> measurements)
        {
            if (measurements is null)
                return;

            if (!m_startTimeSent && measurements.Any())
            {
                m_startTimeSent = true;

                IMeasurement measurement = measurements.FirstOrDefault(m => m is not null);
                Ticks timestamp = 0;

                if (measurement is not null)
                    timestamp = measurement.Timestamp;

                m_parent.SendDataStartTime(ClientID, timestamp);
            }

            if (m_isNaNFiltered)
                measurements = measurements.Where(measurement => !double.IsNaN(measurement.Value));

            // Order measurements by signal type for better compression for non-TSSC compression modes
            if (m_usePayloadCompression && !m_compressionModes.HasFlag(CompressionModes.TSSC))
                base.QueueMeasurementsForProcessing(measurements.OrderBy(m => m.GetSignalType(DataSource)));
            else
                base.QueueMeasurementsForProcessing(measurements);
        }

        /// <summary>
        /// Handles the confirmation message received from the
        /// subscriber to indicate that a buffer block was received.
        /// </summary>
        /// <param name="sequenceNumber">The sequence number of the buffer block.</param>
        /// <returns>A list of buffer block sequence numbers for blocks that need to be retransmitted.</returns>
        public void ConfirmBufferBlock(uint sequenceNumber)
        {
            // We are still receiving confirmations,
            // so stop the retransmission timer
            m_bufferBlockRetransmissionTimer.Stop();

            lock (m_bufferBlockCacheLock)
            {
                // Find the buffer block's location in the cache
                int sequenceIndex = (int)(sequenceNumber - m_expectedBufferBlockConfirmationNumber);

                if (sequenceIndex >= 0 && sequenceIndex < m_bufferBlockCache.Count && m_bufferBlockCache[sequenceIndex] is not null)
                {
                    // Remove the confirmed block from the cache
                    m_bufferBlockCache[sequenceIndex] = null;

                    if (sequenceNumber == m_expectedBufferBlockConfirmationNumber)
                    {
                        // Get the number of elements to trim from the start of the cache
                        int removalCount = m_bufferBlockCache.TakeWhile(m => m is null).Count();

                        // Trim the cache
                        m_bufferBlockCache.RemoveRange(0, removalCount);

                        // Increase the expected confirmation number
                        m_expectedBufferBlockConfirmationNumber += (uint)removalCount;
                    }
                    else
                    {
                        // Retransmit if confirmations are received out of order
                        for (int i = 0; i < sequenceIndex; i++)
                        {
                            if (m_bufferBlockCache[i] is null)
                                continue;

                            m_parent.SendClientResponse(ClientID, ServerResponse.BufferBlock, ServerCommand.Subscribe, m_bufferBlockCache[i]);
                            OnBufferBlockRetransmission();
                        }
                    }
                }

                // If there are any objects lingering in the
                // cache, start the retransmission timer
                if (m_bufferBlockCache.Count > 0)
                    m_bufferBlockRetransmissionTimer.Start();
            }
        }

        /// <summary>
        /// Publish <see cref="IFrame"/> of time-aligned collection of <see cref="IMeasurement"/> values that arrived within the
        /// concentrator's defined <see cref="ConcentratorBase.LagTime"/>.
        /// </summary>
        /// <param name="frame"><see cref="IFrame"/> of measurements with the same timestamp that arrived within <see cref="ConcentratorBase.LagTime"/> that are ready for processing.</param>
        /// <param name="index">Index of <see cref="IFrame"/> within a second ranging from zero to <c><see cref="ConcentratorBase.FramesPerSecond"/> - 1</c>.</param>
        protected override void PublishFrame(IFrame frame, int index)
        {
            if (m_parent is null || m_disposed)
                return;

            if (m_usePayloadCompression && m_compressionModes.HasFlag(CompressionModes.TSSC))
            {
                ProcessTSSCMeasurements(frame);
                return;
            }

            // Includes data packet flags, frame level timestamp and measurement count
            const int PacketHeaderSize = DataPublisher.ClientResponseHeaderSize + 13;

            List<IBinaryMeasurement> packet = new();
            bool usePayloadCompression = m_usePayloadCompression;
            bool useCompactMeasurementFormat = m_useCompactMeasurementFormat || usePayloadCompression;
            long frameLevelTimestamp = frame.Timestamp;
            int packetSize = PacketHeaderSize;

            foreach (IMeasurement measurement in frame.Measurements.Values)
            {
                if (measurement is BufferBlockMeasurement bufferBlockMeasurement)
                {
                    // Still sending buffer block measurements to client; we are expecting
                    // confirmations which will indicate whether retransmission is necessary,
                    // so we will restart the retransmission timer
                    m_bufferBlockRetransmissionTimer.Stop();

                    // Handle buffer block measurements as a special case - this can be any kind of data,
                    // measurement subscriber will need to know how to interpret buffer
                    byte[] bufferBlock = new byte[4 + bufferBlockMeasurement.Length];

                    // Prepend sequence number
                    BigEndian.CopyBytes(m_bufferBlockSequenceNumber, bufferBlock, 0);
                    m_bufferBlockSequenceNumber++;

                    // Append measurement data and send
                    Buffer.BlockCopy(bufferBlockMeasurement.Buffer, 0, bufferBlock, 4, bufferBlockMeasurement.Length);
                    m_parent.SendClientResponse(ClientID, ServerResponse.BufferBlock, ServerCommand.Subscribe, bufferBlock);

                    // Cache buffer block for retransmission
                    lock (m_bufferBlockCacheLock)
                        m_bufferBlockCache.Add(bufferBlock);

                    // Start the retransmission timer in case we never receive a confirmation
                    m_bufferBlockRetransmissionTimer.Start();
                }
                else
                {
                    // Serialize the current measurement.
                    IBinaryMeasurement binaryMeasurement = useCompactMeasurementFormat ? 
                        new CompactMeasurement(measurement, SignalIndexCache, false) : 
                        new SerializableMeasurement(measurement, m_parent.GetClientEncoding(ClientID));

                    // Determine the size of the measurement in bytes.
                    int binaryLength = binaryMeasurement.BinaryLength;

                    // If the current measurement will not fit in the packet based on
                    // the max packet size, process the packet and start a new one.
                    if (packetSize + binaryLength > DataPublisher.MaxPacketSize)
                    {
                        ProcessBinaryMeasurements(packet, frameLevelTimestamp, useCompactMeasurementFormat, usePayloadCompression);
                        packet.Clear();
                        packetSize = PacketHeaderSize;
                    }

                    // Add the measurement to the packet.
                    packet.Add(binaryMeasurement);
                    packetSize += binaryLength;
                }
            }

            // Process the remaining measurements.
            if (packet.Count > 0)
                ProcessBinaryMeasurements(packet, frameLevelTimestamp, useCompactMeasurementFormat, usePayloadCompression);

            // Update latency statistics
            long publishTime = DateTime.UtcNow.Ticks;
            m_parent.UpdateLatencyStatistics(frame.Measurements.Values.Select(m => (long)(publishTime - m.Timestamp)));
        }

        private void ProcessBinaryMeasurements(IEnumerable<IBinaryMeasurement> measurements, long frameLevelTimestamp, bool useCompactMeasurementFormat, bool usePayloadCompression)
        {
            // Create working buffer
            using BlockAllocatedMemoryStream workingBuffer = new();

            // Serialize data packet flags into response
            DataPacketFlags flags = DataPacketFlags.Synchronized;

            if (useCompactMeasurementFormat)
                flags |= DataPacketFlags.Compact;

            workingBuffer.WriteByte((byte)flags);

            // Serialize frame timestamp into data packet - this only occurs in synchronized data packets,
            // unsynchronized subscriptions always include timestamps in the serialized measurements
            workingBuffer.Write(BigEndian.GetBytes(frameLevelTimestamp), 0, 8);

            // Serialize total number of measurement values to follow
            workingBuffer.Write(BigEndian.GetBytes(measurements.Count()), 0, 4);

            if (usePayloadCompression && m_compressionModes.HasFlag(CompressionModes.TSSC))
                throw new InvalidOperationException("TSSC must be processed at the frame level. Please check call stack - this is considered an error.");

            // Attempt compression when requested - encoding of compressed buffer only happens if size would be smaller than normal serialization
            if (!usePayloadCompression || !measurements.Cast<CompactMeasurement>().CompressPayload(workingBuffer, m_compressionStrength, false, ref flags))
            {
                // Serialize measurements to data buffer
                foreach (IBinaryMeasurement measurement in measurements)
                    measurement.CopyBinaryImageToStream(workingBuffer);
            }

            // Update data packet flags if it has updated compression flags
            if ((flags & DataPacketFlags.Compressed) > 0)
            {
                workingBuffer.Seek(0, SeekOrigin.Begin);
                workingBuffer.WriteByte((byte)flags);
            }

            // Publish data packet to client
            m_parent?.SendClientResponse(ClientID, ServerResponse.DataPacket, ServerCommand.Subscribe, workingBuffer.ToArray());
        }

        private void ProcessTSSCMeasurements(IFrame frame)
        {
            lock (m_tsscSyncLock)
            {
                try
                {
                    if (!Enabled)
                        return;

                    if (m_tsscEncoder is null || m_resetTsscEncoder)
                    {
                        m_resetTsscEncoder = false;
                        m_tsscEncoder = new TsscEncoder();
                        m_tsscWorkingBuffer = new byte[32 * 1024];
                        OnStatusMessage(MessageLevel.Info, $"TSSC algorithm reset before sequence number: {m_tsscSequenceNumber}", nameof(TSSC));
                        m_tsscSequenceNumber = 0;
                        m_tsscEncoder.SetBuffer(m_tsscWorkingBuffer, 0, m_tsscWorkingBuffer.Length);
                    }
                    else
                    {
                        m_tsscEncoder.SetBuffer(m_tsscWorkingBuffer, 0, m_tsscWorkingBuffer.Length);
                    }

                    int count = 0;

                    foreach (IMeasurement measurement in frame.Measurements.Values)
                    {
                        ushort index = SignalIndexCache.GetSignalIndex(measurement.Key);

                        if (!m_tsscEncoder.TryAddMeasurement(index, measurement.Timestamp.Value, (uint)measurement.StateFlags, (float)measurement.AdjustedValue))
                        {
                            SendTSSCPayload(frame.Timestamp, count);
                            count = 0;
                            m_tsscEncoder.SetBuffer(m_tsscWorkingBuffer, 0, m_tsscWorkingBuffer.Length);

                            // This will always succeed
                            m_tsscEncoder.TryAddMeasurement(index, measurement.Timestamp.Value, (uint)measurement.StateFlags, (float)measurement.AdjustedValue);
                        }

                        count++;
                    }

                    if (count > 0)
                        SendTSSCPayload(frame.Timestamp, count);

                    // Update latency statistics
                    long publishTime = DateTime.UtcNow.Ticks;
                    m_parent.UpdateLatencyStatistics(frame.Measurements.Values.Select(m => (long)(publishTime - m.Timestamp)));
                }
                catch (Exception ex)
                {
                    string message = $"Error processing measurements: {ex.Message}";
                    OnProcessException(MessageLevel.Info, new InvalidOperationException(message, ex));
                }
            }
        }

        private void SendTSSCPayload(long frameLevelTimestamp, int count)
        {
            int length = m_tsscEncoder.FinishBlock();
            byte[] packet = new byte[length + 16];

            packet[0] = (byte)(DataPacketFlags.Synchronized | DataPacketFlags.Compressed);

            // Serialize frame timestamp into data packet - this only occurs in synchronized data packets,
            // unsynchronized subscriptions always include timestamps in the serialized measurements
            BigEndian.CopyBytes(frameLevelTimestamp, packet, 1);

            // Serialize total number of measurement values to follow
            BigEndian.CopyBytes(count, packet, 1 + 8);

            packet[9 + 4] = 85; // A version number
            BigEndian.CopyBytes(m_tsscSequenceNumber, packet, 13 + 1);
            m_tsscSequenceNumber++;

            //Do not increment to 0
            if (m_tsscSequenceNumber == 0)
                m_tsscSequenceNumber = 1;

            Array.Copy(m_tsscWorkingBuffer, 0, packet, 16, length);

            m_parent?.SendClientResponse(ClientID, ServerResponse.DataPacket, ServerCommand.Subscribe, packet);
        }

        // Retransmits all buffer blocks for which confirmation has not yet been received
        private void BufferBlockRetransmissionTimer_Elapsed(object sender, EventArgs<DateTime> elapsedEventArgs)
        {
            lock (m_bufferBlockCacheLock)
            {
                foreach (byte[] bufferBlock in m_bufferBlockCache.Where(bufferBlock => bufferBlock is not null))
                {
                    m_parent.SendClientResponse(ClientID, ServerResponse.BufferBlock, ServerCommand.Subscribe, bufferBlock);
                    OnBufferBlockRetransmission();
                }
            }

            // Restart the retransmission timer
            m_bufferBlockRetransmissionTimer.Start();
        }

        private void OnBufferBlockRetransmission() => 
            BufferBlockRetransmission?.Invoke(this, EventArgs.Empty);

        // Explicitly implement status message event bubbler to satisfy IClientSubscription interface
        void IClientSubscription.OnStatusMessage(MessageLevel level, string status, string eventName, MessageFlags flags) => 
            OnStatusMessage(level, status, eventName, flags);

        // Explicitly implement process exception event bubbler to satisfy IClientSubscription interface
        void IClientSubscription.OnProcessException(MessageLevel level, Exception ex, string eventName, MessageFlags flags) => 
            OnProcessException(level, ex, eventName, flags);

        // Explicitly implement processing completed event bubbler to satisfy IClientSubscription interface
        void IClientSubscription.OnProcessingCompleted(object sender, EventArgs e) => 
            ProcessingComplete?.Invoke(sender, new EventArgs<IClientSubscription, EventArgs>(this, e));

        #endregion
    }
}
