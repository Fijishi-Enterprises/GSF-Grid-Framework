﻿//******************************************************************************************************
//  DataCell.cs - Gbtc
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
//  11/12/2004 - J. Ritchie Carroll
//       Generated original version of source code.
//  09/15/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  12/17/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

// ReSharper disable VirtualMemberCallInConstructor
namespace GSF.PhasorProtocols.IEEEC37_118
{
    /// <summary>
    /// Represents the IEEE C37.118 implementation of a <see cref="IDataCell"/> that can be sent or received.
    /// </summary>
    [Serializable]
    public class DataCell : DataCellBase
    {
        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="DataCell"/>.
        /// </summary>
        /// <param name="parent">The reference to parent <see cref="IDataFrame"/> of this <see cref="DataCell"/>.</param>
        /// <param name="configurationCell">The <see cref="IConfigurationCell"/> associated with this <see cref="DataCell"/>.</param>
        public DataCell(IDataFrame parent, IConfigurationCell configurationCell)
            : base(parent, configurationCell, 0x0000, Common.MaximumPhasorValues, Common.MaximumAnalogValues, Common.MaximumDigitalValues)
        {
            // Define new parsing state which defines constructors for key data values
            State = new DataCellParsingState(
                configurationCell,
                PhasorValue.CreateNewValue,
                IEEEC37_118.FrequencyValue.CreateNewValue,
                AnalogValue.CreateNewValue,
                DigitalValue.CreateNewValue);
        }

        /// <summary>
        /// Creates a new <see cref="DataCell"/> from specified parameters.
        /// </summary>
        /// <param name="parent">The reference to parent <see cref="DataFrame"/> of this <see cref="DataCell"/>.</param>
        /// <param name="configurationCell">The <see cref="ConfigurationCell"/> associated with this <see cref="DataCell"/>.</param>
        /// <param name="addEmptyValues">If <c>true</c>, adds empty values for each defined configuration cell definition.</param>
        public DataCell(DataFrame parent, ConfigurationCell configurationCell, bool addEmptyValues)
            : this(parent, configurationCell)
        {
            if (!addEmptyValues)
                return;

            // Define needed phasor values
            foreach (IPhasorDefinition phasorDefinition in configurationCell.PhasorDefinitions)
                PhasorValues.Add(new PhasorValue(this, phasorDefinition));

            // Define a frequency and df/dt
            FrequencyValue = new FrequencyValue(this, configurationCell.FrequencyDefinition);

            // Define any analog values
            foreach (IAnalogDefinition analogDefinition in configurationCell.AnalogDefinitions)
                AnalogValues.Add(new AnalogValue(this, analogDefinition));

            // Define any digital values
            foreach (IDigitalDefinition digitalDefinition in configurationCell.DigitalDefinitions)
                DigitalValues.Add(new DigitalValue(this, digitalDefinition));
        }

        /// <summary>
        /// Creates a new <see cref="DataCell"/> from serialization parameters.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> with populated with data.</param>
        /// <param name="context">The source <see cref="StreamingContext"/> for this deserialization.</param>
        protected DataCell(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the reference to parent <see cref="DataFrame"/> of this <see cref="DataCell"/>.
        /// </summary>
        public new DataFrame Parent
        {
            get => base.Parent as DataFrame;
            set => base.Parent = value;
        }

        /// <summary>
        /// Gets or sets the <see cref="ConfigurationCell"/> associated with this <see cref="DataCell"/>.
        /// </summary>
        public new ConfigurationCell ConfigurationCell
        {
            get => base.ConfigurationCell as ConfigurationCell;
            set => base.ConfigurationCell = value;
        }

        /// <summary>
        /// Gets or sets the <see cref="ConfigurationCell3"/> parent of this <see cref="DataCell"/>, if applicable.
        /// </summary>
        public ConfigurationCell3 Parent3
        {
            get => base.ConfigurationCell as ConfigurationCell3;
            set => base.ConfigurationCell = value;
        }

        /// <summary>
        /// Gets or sets status flags for this <see cref="DataCell"/>.
        /// </summary>
        public new StatusFlags StatusFlags
        {
            get => (StatusFlags)(base.StatusFlags & ~(ushort)(StatusFlags.TimeQualityMask | StatusFlags.UnlockedTimeMask | StatusFlags.TriggerReasonMask));
            set => base.StatusFlags = (ushort)((base.StatusFlags & (ushort)(StatusFlags.TimeQualityMask | StatusFlags.UnlockedTimeMask | StatusFlags.TriggerReasonMask)) | (ushort)value);
        }

        /// <summary>
        /// Gets or sets time quality of this <see cref="DataCell"/>.
        /// </summary>
        public TimeQuality TimeQuality
        {
            get => (TimeQuality)(base.StatusFlags & (ushort)StatusFlags.TimeQualityMask);
            set => base.StatusFlags = (ushort)((base.StatusFlags & ~(ushort)StatusFlags.TimeQualityMask) | (ushort)value);
        }

        /// <summary>
        /// Gets or sets unlocked time of this <see cref="DataCell"/>.
        /// </summary>
        public UnlockedTime UnlockedTime
        {
            get => (UnlockedTime)(base.StatusFlags & (ushort)StatusFlags.UnlockedTimeMask);
            set
            {
                base.StatusFlags = (ushort)((base.StatusFlags & ~(ushort)StatusFlags.UnlockedTimeMask) | (ushort)value);
                SynchronizationIsValid = value == UnlockedTime.SyncLocked;
            }
        }

        /// <summary>
        /// Gets or sets trigger reason of this <see cref="DataCell"/>.
        /// </summary>
        public TriggerReason TriggerReason
        {
            get => (TriggerReason)(base.StatusFlags & (short)StatusFlags.TriggerReasonMask);
            set
            {
                base.StatusFlags = (ushort)((base.StatusFlags & ~(short)StatusFlags.TriggerReasonMask) | (ushort)value);
                DeviceTriggerDetected = value != TriggerReason.Manual;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if data of this <see cref="DataCell"/> is valid.
        /// </summary>
        public override bool DataIsValid
        {
            get => (StatusFlags & StatusFlags.DataIsValid) == 0;
            set
            {
                if (value)
                    StatusFlags &= ~StatusFlags.DataIsValid;
                else
                    StatusFlags |= StatusFlags.DataIsValid;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if timestamp of this <see cref="DataCell"/> is valid based on GPS lock.
        /// </summary>
        public override bool SynchronizationIsValid
        {
            get => (StatusFlags & StatusFlags.DeviceSynchronizationError) == 0;
            set
            {
                if (value)
                    StatusFlags &= ~StatusFlags.DeviceSynchronizationError;
                else
                    StatusFlags |= StatusFlags.DeviceSynchronizationError;
            }
        }

        /// <summary>
        /// Gets or sets <see cref="PhasorProtocols.DataSortingType"/> of this <see cref="DataCell"/>.
        /// </summary>
        public override DataSortingType DataSortingType
        {
            get => (StatusFlags & StatusFlags.DataSortingType) == 0 ? DataSortingType.ByTimestamp : DataSortingType.ByArrival;
            set
            {
                if (value == DataSortingType.ByTimestamp)
                    StatusFlags &= ~StatusFlags.DataSortingType;
                else
                    StatusFlags |= StatusFlags.DataSortingType;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if source device of this <see cref="DataCell"/> is reporting an error.
        /// </summary>
        public override bool DeviceError
        {
            get => (StatusFlags & StatusFlags.DeviceError) > 0;
            set
            {
                if (value)
                    StatusFlags |= StatusFlags.DeviceError;
                else
                    StatusFlags &= ~StatusFlags.DeviceError;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if device trigger is detected for this <see cref="DataCell"/>.
        /// </summary>
        public bool DeviceTriggerDetected
        {
            get => (StatusFlags & StatusFlags.DeviceTriggerDetected) > 0;
            set
            {
                if (value)
                    StatusFlags |= StatusFlags.DeviceTriggerDetected;
                else
                    StatusFlags &= ~StatusFlags.DeviceTriggerDetected;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if configuration change was detected for this <see cref="DataCell"/>.
        /// </summary>
        public bool ConfigurationChangeDetected
        {
            get => (StatusFlags & StatusFlags.ConfigurationChanged) > 0;
            set
            {
                if (value)
                    StatusFlags |= StatusFlags.ConfigurationChanged;
                else
                    StatusFlags &= ~StatusFlags.ConfigurationChanged;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if data was modified for this <see cref="DataCell"/>.
        /// </summary>
        public override bool DataModified
        {
            get => (StatusFlags & StatusFlags.DataModified) > 0;
            set
            {
                if (value)
                    StatusFlags |= StatusFlags.DataModified;
                else
                    StatusFlags &= ~StatusFlags.DataModified;
            }
        }

        /// <summary>
        /// <see cref="Dictionary{TKey,TValue}"/> of string based property names and values for the <see cref="DataCell"/> object.
        /// </summary>
        public override Dictionary<string, string> Attributes
        {
            get
            {
                Dictionary<string, string> baseAttributes = base.Attributes;

                baseAttributes.Add("Time Quality", $"{(int)TimeQuality}: {TimeQuality}");
                baseAttributes.Add("Unlocked Time", $"{(int)UnlockedTime}: {UnlockedTime}");
                baseAttributes.Add("Device Trigger Detected", DeviceTriggerDetected.ToString());
                baseAttributes.Add("Trigger Reason", $"{(int)TriggerReason}: {TriggerReason}");
                baseAttributes.Add("Configuration Change Detected", ConfigurationChangeDetected.ToString());

                return baseAttributes;
            }
        }

        #endregion

        #region [ Static ]

        // Static Methods

        // Delegate handler to create a new IEEE C37.118 data cell
        internal static IDataCell CreateNewCell(IChannelFrame parent, IChannelFrameParsingState<IDataCell> state, int index, byte[] buffer, int startIndex, out int parsedLength)
        {
            DataCell dataCell = new(parent as IDataFrame, (state as IDataFrameParsingState)?.ConfigurationFrame.Cells[index]);

            parsedLength = dataCell.ParseBinaryImage(buffer, startIndex, 0);

            return dataCell;
        }

        #endregion
    }
}