﻿//******************************************************************************************************
//  OneSecondDataWindowAdapterBase.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  02/11/2020 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using GSF.Diagnostics;
using GSF.Threading;
using GSF.TimeSeries.Data;
using GSF.Units.EE;
using ConnectionStringParser = GSF.Configuration.ConnectionStringParser<GSF.TimeSeries.Adapters.ConnectionStringParameterAttribute>;

namespace GSF.TimeSeries.Adapters
{
    /// <summary>
    /// Represents an adapter base class that provides functionality to operate on a 1-second window of data.
    /// </summary>
    public abstract class OneSecondDataWindowAdapterBase : ActionAdapterBase
    {
        #region [ Members ]

        // Constants

        /// <summary>
        /// Defines the default value for the <see cref="SourceMeasurementTable"/>.
        /// </summary>
        public const string DefaultSourceMeasurementTable = "ActiveMeasurements";

        // Fields
        private readonly ConcurrentDictionary<long, IMeasurement[,]> m_dataWindows = new();
        private readonly Dictionary<MeasurementKey, int> m_keyIndexes = new();
        private ShortSynchronizedOperation m_processDataWindows;
        private long m_lastFrameTimestamp;
        private long m_processedDataWindows;
        private bool m_supportsTemporalProcessing;

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the expected number of input measurements for a <see cref="OneSecondDataWindowAdapterBase"/> instance.
        /// </summary>
        public int InputCount { get; protected set; }

        /// <summary>
        /// Gets or sets the expected number of output measurements for a <see cref="OneSecondDataWindowAdapterBase"/> instance.
        /// </summary>
        public int OutputCount { get; protected set; }

        /// <summary>
        /// Gets sub-second offsets based on current <see cref="ActionAdapterBase.FramesPerSecond"/>.
        /// </summary>
        public Ticks[] SubsecondOffsets { get; private set; }

        /// <summary>
        /// Gets or sets primary keys of input measurements the action adapter expects.
        /// </summary>
        /// <remarks>
        /// If your adapter needs to receive all measurements, you must explicitly set InputMeasurementKeys to null.
        /// </remarks>
        [ConnectionStringParameter]
        [Description("Defines primary keys of input measurements the action adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
        [DefaultValue(null)]
        [CustomConfigurationEditor("GSF.TimeSeries.UI.WPF.dll", "GSF.TimeSeries.UI.Editors.MeasurementEditor")]
        public override MeasurementKey[] InputMeasurementKeys
        {
            get => base.InputMeasurementKeys;
            set
            {
                base.InputMeasurementKeys = value;
                InputMeasurementKeyTypes = DataSource.GetSignalTypes(value, SourceMeasurementTable);

                for (int i = 0; i < value?.Length; i++)
                    m_keyIndexes[value[i]] = i;
            }
        }

        /// <summary>
        /// Gets or sets output measurements that the action adapter will produce, if any.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines primary keys of output measurements the action adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
        [DefaultValue(null)]
        [CustomConfigurationEditor("GSF.TimeSeries.UI.WPF.dll", "GSF.TimeSeries.UI.Editors.MeasurementEditor")]
        public override IMeasurement[] OutputMeasurements
        {
            get => base.OutputMeasurements;
            set
            {
                base.OutputMeasurements = value;
                OutputMeasurementTypes = DataSource.GetSignalTypes(value, SourceMeasurementTable);
            }
        }

        /// <summary>
        /// Gets or sets the source measurement table to use for configuration.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the source measurement table to use for configuration.")]
        [DefaultValue(DefaultSourceMeasurementTable)]
        public virtual string SourceMeasurementTable { get; set; } = DefaultSourceMeasurementTable;

        /// <summary>
        /// Gets or sets input measurement <see cref="SignalType"/>'s for each of the <see cref="ActionAdapterBase.InputMeasurementKeys"/>, if any.
        /// </summary>
        public virtual SignalType[] InputMeasurementKeyTypes { get; private set; }

        /// <summary>
        /// Gets or sets output measurement <see cref="SignalType"/>'s for each of the <see cref="ActionAdapterBase.OutputMeasurements"/>, if any.
        /// </summary>
        public virtual SignalType[] OutputMeasurementTypes { get; private set; }

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        public override bool SupportsTemporalProcessing => m_supportsTemporalProcessing;

        /// <summary>
        /// Returns the detailed status of the <see cref="OneSecondDataWindowAdapterBase"/>.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new();

                status.AppendLine($"       Queued Data Windows: {m_dataWindows.Count:N0}");
                status.AppendLine($"      Last Frame Timestamp: {new DateTime(Interlocked.Read(ref m_lastFrameTimestamp)):yyyy-MM-dd HH:mm:ss}");
                status.AppendLine($"    Processed Data Windows: {Interlocked.Read(ref m_processedDataWindows):N0}");
                status.AppendLine($"  Source Measurement Table: {SourceMeasurementTable}");
                status.Append(base.Status);

                if (OutputMeasurements is not null && OutputMeasurements.Length > 0)
                {
                    status.AppendLine();
                    status.AppendLine("Output measurements signal type summary:");
                    status.AppendLine();

                    foreach (SignalType signalType in Enum.GetValues(typeof(SignalType)))
                    {
                        int count = OutputMeasurements.Where((_, index) => OutputMeasurementTypes[index] == signalType).Count();

                        if (count <= 0)
                            continue;

                        status.AppendLine($"{count,15} {signalType.GetFormattedName()} signal{(count > 1 ? "s" : "")}");
                    }
                }

                if (InputMeasurementKeys is not null && InputMeasurementKeys.Length > 0)
                {
                    status.AppendLine();
                    status.AppendLine("Input measurement keys signal type summary:");
                    status.AppendLine();

                    foreach (SignalType signalType in Enum.GetValues(typeof(SignalType)))
                    {
                        int count = InputMeasurementKeys.Where((_, index) => InputMeasurementKeyTypes[index] == signalType).Count();

                        if (count <= 0)
                            continue;

                        status.AppendLine($"{count,15} {signalType.GetFormattedName()} signal{(count > 1 ? "s" : "")}");
                    }
                }

                return status.ToString();
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Initializes <see cref="OneSecondDataWindowAdapterBase" />.
        /// </summary>
        public override void Initialize()
        {
            string setting;

            // Parse all properties marked with ConnectionStringParameterAttribute from provided ConnectionString value
            ConnectionStringParser parser = new();
            parser.ParseConnectionString(ConnectionString, this);

            base.Initialize();

            // Reparse configured inputs and outputs if a non-standard source measurement table is being used
            if (!SourceMeasurementTable.Equals(DefaultSourceMeasurementTable, StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, string> settings = Settings;

                if (settings.TryGetValue(nameof(InputMeasurementKeys), out setting))
                    InputMeasurementKeys = AdapterBase.ParseInputMeasurementKeys(DataSource, true, setting, SourceMeasurementTable);

                if (settings.TryGetValue(nameof(OutputMeasurements), out setting))
                    OutputMeasurements = AdapterBase.ParseOutputMeasurements(DataSource, true, setting, SourceMeasurementTable);
            }

            int configuredInputCount = InputMeasurementKeys?.Length ?? 0;
            int configuredOutputCount = OutputMeasurements?.Length ?? 0;

            if (configuredInputCount == 0)
                throw new InvalidOperationException("No inputs specified. Cannot initialize adapter.");

            if (configuredInputCount != InputCount)
                throw new InvalidOperationException($"Expected {InputCount:N0} input measurements, there are {configuredInputCount:N0} defined. Cannot initialize adapter.");

            if (configuredOutputCount != OutputCount)
                throw new InvalidOperationException($"Expected {OutputCount:N0} output measurements, there are {configuredOutputCount:N0} defined. Cannot initialize adapter.");

            m_supportsTemporalProcessing = Settings.TryGetValue("supportsTemporalProcessing", out setting) && setting.ParseBoolean();

            SubsecondOffsets = Ticks.SubsecondDistribution(FramesPerSecond);

            // Define a synchronized operation to process completed data windows
            m_processDataWindows = new ShortSynchronizedOperation(ProcessDataWindows, ex => OnProcessException(MessageLevel.Warning, ex));
        }

        /// <summary>
        /// Processes 1-second data window.
        /// </summary>
        /// <param name="timestamp">Top of second window timestamp.</param>
        /// <param name="dataWindow">1-second data window.</param>
        protected abstract void ProcessDataWindow(Ticks timestamp, IMeasurement[,] dataWindow);

        /// <summary>
        /// Publish <see cref="IFrame" /> of time-aligned collection of <see cref="IMeasurement" />
        /// values that arrived within defined <see cref="ConcentratorBase.LagTime" />.
        /// </summary>
        /// <param name="frame">Collection of measurements with the same timestamp that arrived within <see cref="ConcentratorBase.LagTime" /> that are ready for processing.</param>
        /// <param name="index">Index of <see cref="IFrame" /> within a second ranging from zero to <c><see cref="ConcentratorBase.FramesPerSecond" /> - 1</c>.</param>
        /// <remarks>
        /// If user implemented publication function consistently exceeds available publishing time (i.e., <c>1 / <see cref="ConcentratorBase.FramesPerSecond" /></c> seconds),
        /// concentration will fall behind. A small amount of this time is required by the <see cref="ConcentratorBase" /> for processing overhead, so actual total time
        /// available for user function process will always be slightly less than <c>1 / <see cref="ConcentratorBase.FramesPerSecond" /></c> seconds.
        /// </remarks>
        protected override void PublishFrame(IFrame frame, int index)
        {
            // Baseline timestamp to the top of the second - this represents the window key
            Interlocked.Exchange(ref m_lastFrameTimestamp, frame.Timestamp.BaselinedTimestamp(BaselineTimeInterval.Second));

            // Get current data window, creating it if needed
            IMeasurement[,] dataWindow = m_dataWindows.GetOrAdd(Interlocked.Read(ref m_lastFrameTimestamp), CreateNewDataWindow);

            // At the top of each second, process any prior data windows
            if (index == 0)
                m_processDataWindows.RunOnceAsync();

            // Update current data window with provided measurements
            foreach (IMeasurement measurement in frame.Measurements.Values)
                dataWindow[m_keyIndexes[measurement.Key], index] = measurement;
        }

        private void ProcessDataWindows()
        {
            long lastFrameTimestamp = Interlocked.Read(ref m_lastFrameTimestamp);
            long[] timestamps = m_dataWindows.Keys.Where(key => key < lastFrameTimestamp).ToArray();

            // Process all completed data windows
            foreach (long timestamp in timestamps)
            {
                if (!m_dataWindows.TryRemove(timestamp, out IMeasurement[,] dataWindow))
                    continue;

                Debug.Assert(dataWindow.GetLength(0) == InputCount, $"Unexpected data window input size: {dataWindow.GetLength(0)}, expected {InputCount}");
                Debug.Assert(dataWindow.GetLength(1) == FramesPerSecond, $"Unexpected data window row size: {dataWindow.GetLength(1)}, expected {FramesPerSecond}");

                ProcessDataWindow(timestamp, dataWindow);
                    
                Interlocked.Increment(ref m_processedDataWindows);
            }
        }

        private IMeasurement[,] CreateNewDataWindow(long timestamp)
        {
            IMeasurement[,] dataWindow = new IMeasurement[InputCount, FramesPerSecond];

            for (int i = 0; i < InputCount; i++)
            {
                MeasurementKey key = InputMeasurementKeys[i];

                for (int j = 0; j < FramesPerSecond; j++)
                {
                    dataWindow[i, j] = new Measurement
                    {
                        Metadata = key.Metadata,
                        Value = double.NaN,
                        Timestamp = timestamp + SubsecondOffsets[j],
                        StateFlags = MeasurementStateFlags.BadData
                    };
                }
            }

            return dataWindow;
        }

        #endregion
    }
}
