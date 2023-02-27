﻿//******************************************************************************************************
//  ClientConnection.cs - Gbtc
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
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using GSF.Communication;
using GSF.Diagnostics;
using GSF.IO;
using GSF.Security.Cryptography;
using GSF.Threading;

namespace GSF.TimeSeries.Transport
{
    /// <summary>
    /// Represents a <see cref="DataSubscriber"/> client connection to the <see cref="DataPublisher"/>.
    /// </summary>
    public class ClientConnection : IProvideStatus, IDisposable
    {
        #region [ Members ]

        // Constants
        private const int EvenKey = 0;      // Even key/IV index
        private const int OddKey = 1;       // Odd key/IV index
        private const int KeyIndex = 0;     // Index of cipher key component in keyIV array
        private const int IVIndex = 1;      // Index of initialization vector component in keyIV array

        // Fields
        private DataPublisher m_parent;
        private Guid m_subscriberID;
        private readonly string m_hostName;
        private string m_subscriberInfo;
        private IClientSubscription m_subscription;
        private volatile bool m_authenticated;
        private volatile byte[][][] m_keyIVs;
        private volatile int m_cipherIndex;
        private UdpServer m_dataChannel;
        private string m_configurationString;
        private bool m_connectionEstablished;
        private SharedTimer m_pingTimer;
        private SharedTimer m_reconnectTimer;
        private OperationalModes m_operationalModes;
        private Encoding m_encoding;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="ClientConnection"/> instance.
        /// </summary>
        /// <param name="parent">Parent data publisher.</param>
        /// <param name="clientID">Client ID of associated connection.</param>
        /// <param name="commandChannel"><see cref="TcpServer"/> command channel used to lookup connection information.</param>
        public ClientConnection(DataPublisher parent, Guid clientID, IServer commandChannel)
        {
            m_parent = parent;
            ClientID = clientID;
            CommandChannel = commandChannel;
            m_subscriberID = clientID;
            m_keyIVs = null;
            m_cipherIndex = 0;

            // Setup ping timer
            m_pingTimer = Common.TimerScheduler.CreateTimer(5000);
            m_pingTimer.AutoReset = true;
            m_pingTimer.Elapsed += m_pingTimer_Elapsed;
            m_pingTimer.Start();

            // Setup reconnect timer
            m_reconnectTimer = Common.TimerScheduler.CreateTimer(1000);
            m_reconnectTimer.AutoReset = false;
            m_reconnectTimer.Elapsed += m_reconnectTimer_Elapsed;

            // Attempt to lookup remote connection identification for logging purposes
            try
            {
                Socket commandChannelSocket = GetCommandChannelSocket();
                IPEndPoint remoteEndPoint = null;

                if (commandChannel is not null)
                    remoteEndPoint = commandChannelSocket.RemoteEndPoint as IPEndPoint;

                if (remoteEndPoint is not null)
                {
                    IPAddress = remoteEndPoint.Address;

                    ConnectionID = remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ?
                        $"[{IPAddress}]:{remoteEndPoint.Port}" :
                        $"{IPAddress}:{remoteEndPoint.Port}";

                    try
                    {
                        IPHostEntry ipHost = Dns.GetHostEntry(remoteEndPoint.Address);

                        if (!string.IsNullOrWhiteSpace(ipHost.HostName))
                        {
                            m_hostName = ipHost.HostName;
                            ConnectionID = $"{m_hostName} ({ConnectionID})";
                        }
                    }

                    // Just ignoring possible DNS lookup failures...
                    catch (ArgumentNullException)
                    {
                        // The hostNameOrAddress parameter is null. 
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // The length of hostNameOrAddress parameter is greater than 255 characters. 
                    }
                    catch (ArgumentException)
                    {
                        // The hostNameOrAddress parameter is an invalid IP address. 
                    }
                    catch (SocketException)
                    {
                        // An error was encountered when resolving the hostNameOrAddress parameter.    
                    }
                }
            }
            catch
            {
                // At worst we'll just use the client GUID for identification
                ConnectionID = m_subscriberID == Guid.Empty ? clientID.ToString() : m_subscriberID.ToString();
            }

            if (string.IsNullOrWhiteSpace(ConnectionID))
                ConnectionID = "unavailable";

            if (string.IsNullOrWhiteSpace(m_hostName))
            {
                m_hostName = IPAddress is not null ? 
                    IPAddress.ToString() : 
                    ConnectionID;
            }

            IPAddress ??= IPAddress.None;
        }

        /// <summary>
        /// Releases the unmanaged resources before the <see cref="ClientConnection"/> object is reclaimed by <see cref="GC"/>.
        /// </summary>
        ~ClientConnection() => 
            Dispose(false);

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets client ID of this <see cref="ClientConnection"/>.
        /// </summary>
        public Guid ClientID { get; }

        /// <summary>
        /// Gets or sets reference to <see cref="UdpServer"/> data channel, attaching to or detaching from events as needed, associated with this <see cref="ClientConnection"/>.
        /// </summary>
        public UdpServer DataChannel
        {
            get => m_dataChannel;
            set
            {
                m_connectionEstablished = value is not null;

                if (m_dataChannel is not null)
                {
                    // Detach from events on existing data channel reference
                    m_dataChannel.ClientConnectingException -= m_dataChannel_ClientConnectingException;
                    m_dataChannel.SendClientDataException -= m_dataChannel_SendClientDataException;
                    m_dataChannel.ServerStarted -= m_dataChannel_ServerStarted;
                    m_dataChannel.ServerStopped -= m_dataChannel_ServerStopped;

                    if (m_dataChannel != value)
                        m_dataChannel.Dispose();
                }

                // Assign new data channel reference
                m_dataChannel = value;

                if (m_dataChannel is not null)
                {
                    // Save UDP settings so channel can be reestablished if needed
                    m_configurationString = m_dataChannel.ConfigurationString;

                    // Attach to events on new data channel reference
                    m_dataChannel.ClientConnectingException += m_dataChannel_ClientConnectingException;
                    m_dataChannel.SendClientDataException += m_dataChannel_SendClientDataException;
                    m_dataChannel.ServerStarted += m_dataChannel_ServerStarted;
                    m_dataChannel.ServerStopped += m_dataChannel_ServerStopped;
                }
            }
        }

        /// <summary>
        /// Gets <see cref="IServer"/> command channel.
        /// </summary>
        public IServer CommandChannel { get; private set; }

        /// <summary>
        /// Gets <see cref="IServer"/> publication channel - that is, data channel if defined otherwise command channel.
        /// </summary>
        public IServer PublishChannel => m_dataChannel ?? CommandChannel;

        /// <summary>
        /// Gets connected state of the associated client socket.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                bool isConnected = false;

                try
                {
                    Socket commandChannelSocket = GetCommandChannelSocket();

                    if (commandChannelSocket is not null)
                        isConnected = commandChannelSocket.Connected;
                }
                catch
                {
                    isConnected = false;
                }

                return isConnected;
            }
        }

        /// <summary>
        /// Gets or sets IsSubscribed state.
        /// </summary>
        public bool IsSubscribed { get; set; }

        /// <summary>
        /// Gets or sets flag that indicates if the socket exception for "No client found for ID [Guid]" has been thrown.
        /// </summary>
        /// <returns>
        /// Since this message might be thrown many times before the communications channel has had a chance to disconnect
        /// the socket, it is best to stop attempting to send data when this error has been encountered.
        /// </returns>
        public bool ClientNotFoundExceptionOccurred
        {
            // Users have encountered issues when a client disconnects where many thousands of exceptions get thrown, every 3ms.
            // This can cause the entire system to become unresponsive and causes all devices to reset (no data).
            // System only recovers when the client disconnect process finally executes as this can take some time to occur.
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the <see cref="Guid"/> based subscriber ID of this <see cref="ClientConnection"/>.
        /// </summary>
        public Guid SubscriberID
        {
            get => m_subscriberID;
            set => m_subscriberID = value;
        }

        /// <summary>
        /// Gets or sets the subscriber acronym of this <see cref="ClientConnection"/>.
        /// </summary>
        public string SubscriberAcronym { get; set; }

        /// <summary>
        /// Gets or sets the subscriber name of this <see cref="ClientConnection"/>.
        /// </summary>
        public string SubscriberName { get; set; }

        /// <summary>
        /// Gets or sets subscriber info for this <see cref="ClientConnection"/>.
        /// </summary>
        public string SubscriberInfo
        {
            get
            {
                if (string.IsNullOrWhiteSpace(m_subscriberInfo))
                    return SubscriberName;

                return m_subscriberInfo;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    m_subscriberInfo = null;
                }
                else
                {
                    Dictionary<string, string> settings = value.ParseKeyValuePairs();

                    settings.TryGetValue("source", out string source);
                    settings.TryGetValue("version", out string version);
                    settings.TryGetValue("buildDate", out string buildDate);

                    m_subscriberInfo = $"{source.ToNonNullNorWhiteSpace("unknown source")} version {version.ToNonNullNorWhiteSpace("?.?.?.?")} built on {buildDate.ToNonNullNorWhiteSpace("undefined date")}";
                }
            }
        }

        /// <summary>
        /// Gets the connection identification of this <see cref="ClientConnection"/>.
        /// </summary>
        public string ConnectionID { get; }

        /// <summary>
        /// Gets or sets authenticated state of this <see cref="ClientConnection"/>.
        /// </summary>
        public bool Authenticated
        {
            get => m_authenticated;
            set => m_authenticated = value;
        }

        /// <summary>
        /// Gets or sets shared secret used to lookup cipher keys only known to client and server.
        /// </summary>
        public string SharedSecret { get; set; }

        /// <summary>
        /// Gets active and standby keys and initialization vectors.
        /// </summary>
        public byte[][][] KeyIVs => m_keyIVs;

        /// <summary>
        /// Gets current cipher index.
        /// </summary>
        public int CipherIndex => m_cipherIndex;

        /// <summary>
        /// Gets time of last cipher key update.
        /// </summary>
        public Ticks LastCipherKeyUpdateTime { get; private set; }

        /// <summary>
        /// Gets or sets the list of valid IP addresses that this client can connect from.
        /// </summary>
        public List<IPAddress> ValidIPAddresses { get; set; }

        /// <summary>
        /// Gets the IP address of the remote client connection.
        /// </summary>
        public IPAddress IPAddress { get; private set; }

        /// <summary>
        /// Gets or sets subscription associated with this <see cref="ClientConnection"/>.
        /// </summary>
        public IClientSubscription Subscription
        {
            get => m_subscription;
            set
            {
                m_subscription = value;

                if (m_subscription is not null)
                    m_subscription.Name = m_hostName;
            }
        }

        /// <summary>
        /// Gets the subscriber name of this <see cref="ClientConnection"/>.
        /// </summary>
        public string Name => SubscriberName;

        /// <summary>
        /// Gets or sets a set of flags that define ways in
        /// which the subscriber and publisher communicate.
        /// </summary>
        public OperationalModes OperationalModes
        {
            get => m_operationalModes;
            set
            {
                m_operationalModes = value;

                m_encoding = (OperationalEncoding)(value & OperationalModes.EncodingMask) switch
                {
                    OperationalEncoding.Unicode => Encoding.Unicode,
                    OperationalEncoding.BigEndianUnicode => Encoding.BigEndianUnicode,
                    OperationalEncoding.UTF8 => Encoding.UTF8,
                    OperationalEncoding.ANSI => Encoding.Default,
                    _ => throw new InvalidOperationException($"Unsupported encoding detected: {value}")
                };
            }
        }

        /// <summary>
        /// Character encoding used to send messages to subscriber.
        /// </summary>
        public Encoding Encoding => m_encoding ?? Encoding.Unicode;

        /// <summary>
        /// Gets a formatted message describing the status of this <see cref="ClientConnection"/>.
        /// </summary>
        public string Status
        {
            get
            {
                StringBuilder status = new();

                status.AppendLine();
                status.AppendLine($"             Subscriber ID: {ConnectionID}");
                status.AppendLine($"           Subscriber name: {SubscriberName}");
                status.AppendLine($"        Subscriber acronym: {SubscriberAcronym}");
                status.AppendLine($"  Publish channel protocol: {PublishChannel.TransportProtocol}");
                status.AppendLine($"      Data packet security: {(m_keyIVs is null ? "unencrypted" : "encrypted")}");

                if (m_dataChannel is not null)
                {
                    status.AppendLine();
                    status.Append(m_dataChannel.Status);
                }

                return status.ToString();
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases all the resources used by the <see cref="ClientConnection"/> object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ClientConnection"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (m_disposed)
                return;

            try
            {
                if (!disposing)
                    return;

                if (m_pingTimer is not null)
                {
                    m_pingTimer.Elapsed -= m_pingTimer_Elapsed;
                    m_pingTimer.Dispose();
                    m_pingTimer = null;
                }

                if (m_reconnectTimer is not null)
                {
                    m_reconnectTimer.Elapsed -= m_reconnectTimer_Elapsed;
                    m_reconnectTimer.Dispose();
                    m_reconnectTimer = null;
                }

                DataChannel = null;
                CommandChannel = null;
                IPAddress = null;
                m_subscription = null;
                m_parent = null;
            }
            finally
            {
                m_disposed = true;  // Prevent duplicate dispose.
            }
        }

        /// <summary>
        /// Creates or updates cipher keys.
        /// </summary>
        internal void UpdateKeyIVs()
        {
            using (AesManaged symmetricAlgorithm = new())
            {
                symmetricAlgorithm.KeySize = 256;
                symmetricAlgorithm.GenerateKey();
                symmetricAlgorithm.GenerateIV();

                if (m_keyIVs is null)
                {
                    // Initialize new key set
                    m_keyIVs = new byte[2][][];
                    m_keyIVs[EvenKey] = new byte[2][];
                    m_keyIVs[OddKey] = new byte[2][];

                    m_keyIVs[EvenKey][KeyIndex] = symmetricAlgorithm.Key;
                    m_keyIVs[EvenKey][IVIndex] = symmetricAlgorithm.IV;

                    symmetricAlgorithm.GenerateKey();
                    symmetricAlgorithm.GenerateIV();

                    m_keyIVs[OddKey][KeyIndex] = symmetricAlgorithm.Key;
                    m_keyIVs[OddKey][IVIndex] = symmetricAlgorithm.IV;

                    m_cipherIndex = EvenKey;
                }
                else
                {
                    // Generate a new key set for current cipher index
                    m_keyIVs[m_cipherIndex][KeyIndex] = symmetricAlgorithm.Key;
                    m_keyIVs[m_cipherIndex][IVIndex] = symmetricAlgorithm.IV;

                    // Set run-time to the other key set
                    m_cipherIndex ^= 1;
                }
            }

            LastCipherKeyUpdateTime = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Rotates or initializes the crypto keys for this <see cref="ClientConnection"/>.
        /// </summary>
        public bool RotateCipherKeys()
        {
            // Make sure at least a second has passed before next key rotation
            if ((DateTime.UtcNow.Ticks - LastCipherKeyUpdateTime).ToMilliseconds() >= 1000.0D)
            {
                try
                {
                    // Since this function cannot be not called more than once per second there
                    // is no real benefit to maintaining these memory streams at a member level
                    using (BlockAllocatedMemoryStream response = new())
                    {
                        byte[] bytes;

                        // Create or update cipher keys and initialization vectors 
                        UpdateKeyIVs();

                        // Add current cipher index to response
                        response.WriteByte((byte)m_cipherIndex);

                        // Serialize new keys
                        using (BlockAllocatedMemoryStream buffer = new())
                        {
                            // Write even key
                            byte[] bufferLen = BigEndian.GetBytes(m_keyIVs[EvenKey][KeyIndex].Length);
                            buffer.Write(bufferLen, 0, bufferLen.Length);
                            buffer.Write(m_keyIVs[EvenKey][KeyIndex], 0, m_keyIVs[EvenKey][KeyIndex].Length);

                            // Write even initialization vector
                            bufferLen = BigEndian.GetBytes(m_keyIVs[EvenKey][IVIndex].Length);
                            buffer.Write(bufferLen, 0, bufferLen.Length);
                            buffer.Write(m_keyIVs[EvenKey][IVIndex], 0, m_keyIVs[EvenKey][IVIndex].Length);

                            // Write odd key
                            bufferLen = BigEndian.GetBytes(m_keyIVs[OddKey][KeyIndex].Length);
                            buffer.Write(bufferLen, 0, bufferLen.Length);
                            buffer.Write(m_keyIVs[OddKey][KeyIndex], 0, m_keyIVs[OddKey][KeyIndex].Length);

                            // Write odd initialization vector
                            bufferLen = BigEndian.GetBytes(m_keyIVs[OddKey][IVIndex].Length);
                            buffer.Write(bufferLen, 0, bufferLen.Length);
                            buffer.Write(m_keyIVs[OddKey][IVIndex], 0, m_keyIVs[OddKey][IVIndex].Length);

                            // Get bytes from serialized buffer
                            bytes = buffer.ToArray();
                        }

                        // Encrypt keys using private keys known only to current client and server
                        if (m_authenticated && !string.IsNullOrWhiteSpace(SharedSecret))
                            bytes = bytes.Encrypt(SharedSecret, CipherStrength.Aes256);

                        // Add serialized key response
                        response.Write(bytes, 0, bytes.Length);

                        // Send cipher key updates
                        m_parent.SendClientResponse(ClientID, ServerResponse.UpdateCipherKeys, ServerCommand.Subscribe, response.ToArray());
                    }

                    // Send success message
                    m_parent.SendClientResponse(ClientID, ServerResponse.Succeeded, ServerCommand.RotateCipherKeys, "New cipher keys established.");
                    m_parent.OnStatusMessage(MessageLevel.Info, $"{ConnectionID} cipher keys rotated.");
                    return true;
                }
                catch (Exception ex)
                {
                    // Send failure message
                    m_parent.SendClientResponse(ClientID, ServerResponse.Failed, ServerCommand.RotateCipherKeys, $"Failed to establish new cipher keys: {ex.Message}");
                    m_parent.OnStatusMessage(MessageLevel.Warning, $"Failed to establish new cipher keys for {ConnectionID}: {ex.Message}");
                    return false;
                }
            }

            m_parent.SendClientResponse(ClientID, ServerResponse.Failed, ServerCommand.RotateCipherKeys, "Cipher key rotation skipped, keys were already rotated within last second.");
            m_parent.OnStatusMessage(MessageLevel.Warning, $"Cipher key rotation skipped for {ConnectionID}, keys were already rotated within last second.");
            return false;
        }

        /// <summary>
        /// Gets the <see cref="Socket"/> instance used by this client
        /// connection to send and receive data over the command channel.
        /// </summary>
        /// <returns>The socket instance used by the client to send and receive data over the command channel.</returns>
        public Socket GetCommandChannelSocket()
        {
            TlsServer tlsCommandChannel = CommandChannel as TlsServer;

            if (CommandChannel is TcpServer tcpCommandChannel && tcpCommandChannel.TryGetClient(ClientID, out TransportProvider<Socket> tcpProvider))
                return tcpProvider.Provider;

            if (tlsCommandChannel is not null && tlsCommandChannel.TryGetClient(ClientID, out TransportProvider<TlsServer.TlsSocket> tlsProvider))
                return tlsProvider.Provider.Socket;

            return null;
        }

        // Send a no-op keep-alive ping to make sure the client is still connected
        private void m_pingTimer_Elapsed(object sender, EventArgs<DateTime> e) => 
            m_parent.SendClientResponse(ClientID, ServerResponse.NoOP, ServerCommand.Subscribe);

        private void m_dataChannel_ClientConnectingException(object sender, EventArgs<Exception> e)
        {
            Exception ex = e.Argument;
            m_parent.OnProcessException(MessageLevel.Info, new ConnectionException($"Data channel client connection exception occurred while sending client data to \"{ConnectionID}\": {ex.Message}", ex));
        }

        private void m_dataChannel_SendClientDataException(object sender, EventArgs<Guid, Exception> e)
        {
            Exception ex = e.Argument2;
            m_parent.OnProcessException(MessageLevel.Info, new InvalidOperationException($"Data channel exception occurred while sending client data to \"{ConnectionID}\": {ex.Message}", ex));
        }

        private void m_dataChannel_ServerStarted(object sender, EventArgs e) => 
            m_parent.OnStatusMessage(MessageLevel.Info, "Data channel started.");

        private void m_dataChannel_ServerStopped(object sender, EventArgs e)
        {
            if (m_connectionEstablished)
            {
                m_parent.OnStatusMessage(MessageLevel.Info, "Data channel stopped unexpectedly, restarting data channel...");
                m_reconnectTimer?.Start();
            }
            else
            {
                m_parent.OnStatusMessage(MessageLevel.Info, "Data channel stopped.");
            }
        }

        private void m_reconnectTimer_Elapsed(object sender, EventArgs<DateTime> e)
        {
            try
            {
                m_parent.OnStatusMessage(MessageLevel.Info, "Attempting to restart data channel...");
                DataChannel = null;

                UdpServer dataChannel = new(m_configurationString);
                dataChannel.Start();

                DataChannel = dataChannel;
                m_parent.OnStatusMessage(MessageLevel.Info, "Data channel successfully restarted.");
            }
            catch (Exception ex)
            {
                m_parent.OnStatusMessage(MessageLevel.Warning, $"Failed to restart data channel due to exception: {ex.Message}");
                m_reconnectTimer.Start();
            }
        }

        #endregion
    }
}
