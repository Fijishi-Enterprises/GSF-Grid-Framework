﻿//******************************************************************************************************
//  LossOfField.cs - Gbtc
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
//  12/02/2009 - Jian R. Zuo
//       Generated original version of source code.
//  12/16/2009 - Jian R. Zuo
//       Reading parameters configuration from database
//  04/12/2010 - J. Ritchie Carroll
//       Performed full code review, optimization and further abstracted code for LOF detection.
//  12/13/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using GSF.Collections;
using GSF.Diagnostics;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;
using GSF.Units;
using GSF.Units.EE;
using PhasorProtocolAdapters;

namespace PowerCalculations.EventDetection;

/// <summary>
/// Represents an algorithm that detects Loss of Field from a synchrophasor device.
/// </summary>
[Description("Loss of Field: Detects Loss-of-Field from a synchrophasor device")]
public class LossOfField : CalculatedMeasurementBase
{
    #region [ Members ]

    // Constants
    private const double SqrtOf3 = 1.7320508075688772935274463415059D;

    // Fields
    private double m_qAreamVar;                 // Calculated Q area value                 
    private long m_count;                       // Running frame count
    private long m_count1;                      // Last frame count
    private long m_count2;                      // Current frame count
    private MeasurementKey m_voltageMagnitude;  // Measurement input key for voltage magnitude
    private MeasurementKey m_voltageAngle;      // Measurement input key for voltage angle
    private MeasurementKey m_currentMagnitude;  // Measurement input key for current magnitude
    private MeasurementKey m_currentAngle;      // Measurement input key for current angle

    // Important: Make sure output definition defines points in the following order
    private enum Output
    {
        WarningSignal,
        RealPower,
        ReactivePower,
        QAreaValue
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets the threshold of P-set MW.
    /// </summary>
    [ConnectionStringParameter]
    [Description("Define the threshold of P-set MW.")]
    [DefaultValue(-600)] // default value -600 mW  
    public double PSet { get; set; }

    /// <summary>
    /// Gets or sets the threshold of Q-set MVar.
    /// </summary>
    [ConnectionStringParameter]
    [Description("Define the threshold of Q-set MVar.")]
    [DefaultValue(200)] // default value 200 mVar
    public double QSet { get; set; }

    /// <summary>
    /// Gets or sets the threshold of Q-area MVar-sec.
    /// </summary>
    [ConnectionStringParameter]
    [Description("Define the threshold of Q-area MVar-sec.")]
    [DefaultValue(500)] // default value 500 mVar-sec
    public double QAreaSet { get; set; }

    /// <summary>
    /// Gets or sets the threshold of voltage, in volts.
    /// </summary>
    [ConnectionStringParameter]
    [Description("Define the threshold of voltage, in volts.")]
    [DefaultValue(475000)] // default value 0.95 p.u. or 475 kV
    public double VoltageThreshold { get; set; }

    /// <summary>
    /// Gets or sets the interval between adjacent calculations.
    /// </summary>
    [ConnectionStringParameter]
    [Description("Define the interval between adjacent calculations. The default value is the frame-rate defined in the connection string for this Loss of Field.")]
    [DefaultValue("")]
    public int AnalysisInterval { get; set; }

    /// <summary>
    /// Returns the detailed status of the <see cref="LossOfField"/> detector.
    /// </summary>
    public override string Status
    {
        get
        {
            StringBuilder status = new();

            status.AppendFormat("   Calculated Q-area value: {0}", m_qAreamVar);
            status.AppendLine();
            status.AppendFormat("               P-Set value: {0}", PSet);
            status.AppendLine();
            status.AppendFormat("               Q-Set value: {0}", QSet);
            status.AppendLine();
            status.AppendFormat("          Q-Area set value: {0}", QAreaSet);
            status.AppendLine();
            status.AppendFormat("         Voltage threshold: {0}", VoltageThreshold);
            status.AppendLine();
            status.AppendFormat("      Calculation interval: {0}", AnalysisInterval);
            status.AppendLine();

            status.Append(base.Status);

            return status.ToString();
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Initializes the <see cref="LossOfField"/> detector.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        Dictionary<string, string> settings = Settings;

        // Load parameters
        if (settings.TryGetValue("pSet", out string setting))
            PSet = double.Parse(setting);
        else
            PSet = -600;

        if (settings.TryGetValue("qSet", out setting))
            QSet = double.Parse(setting);
        else
            QSet = 200;

        if (settings.TryGetValue("qAreaSet", out setting))
            QAreaSet = double.Parse(setting);
        else
            QAreaSet = 500;

        if (settings.TryGetValue("voltageThreshold", out setting))
            VoltageThreshold = double.Parse(setting);
        else
            VoltageThreshold = 475000;

        if (settings.TryGetValue("analysisInterval", out setting))
            AnalysisInterval = int.Parse(setting);
        else
            AnalysisInterval = FramesPerSecond;

        m_count = 0;
        m_count1 = 0;
        m_count2 = 0;

        // Load needed measurement keys from defined InputMeasurementKeys
        int index;

        // Get expected voltage magnitude
        index = InputMeasurementKeyTypes.IndexOf(signalType => signalType == SignalType.VPHM);
        if (index < 0)
            throw new InvalidOperationException("No voltage magnitude input measurement key was not found - this is a required input measurement for the loss of field detector.");

        m_voltageMagnitude = InputMeasurementKeys[index];

        // Get expected voltage angle
        index = InputMeasurementKeyTypes.IndexOf(signalType => signalType == SignalType.VPHA);
        if (index < 0)
            throw new InvalidOperationException("No voltage angle input measurement key was not found - this is a required input measurement for the loss of field detector.");

        m_voltageAngle = InputMeasurementKeys[index];

        // Get expected current magnitude
        index = InputMeasurementKeyTypes.IndexOf(signalType => signalType == SignalType.IPHM);
        if (index < 0)
            throw new InvalidOperationException("No current magnitude input measurement key was not found - this is a required input measurement for the loss of field detector.");

        m_currentMagnitude = InputMeasurementKeys[index];

        // Get expected current angle
        index = InputMeasurementKeyTypes.IndexOf(signalType => signalType == SignalType.IPHA);
        if (index < 0)
            throw new InvalidOperationException("No current angle input measurement key was not found - this is a required input measurement for the loss of field detector.");

        m_currentAngle = InputMeasurementKeys[index];

        // Make sure only these phasor measurements are used as input
        InputMeasurementKeys = new[] { m_voltageMagnitude, m_voltageAngle, m_currentMagnitude, m_currentAngle };

        // Validate output measurements
        if (OutputMeasurements.Length < Enum.GetValues(typeof(Output)).Length)
            throw new InvalidOperationException("Not enough output measurements were specified for the loss of field detector, expecting measurements for \"Warning Signal Status (0 = Not Signaled, 1 = Signaled)\", \"Real Power\", \"Reactive Power\" and \"Q-Area Value\" - in this order.");
    }

    /// <summary>
    /// Publishes the <see cref="IFrame"/> of time-aligned collection of <see cref="IMeasurement"/> values that arrived within the
    /// adapter's defined <see cref="ConcentratorBase.LagTime"/>.
    /// </summary>
    /// <param name="frame"><see cref="IFrame"/> of measurements with the same timestamp that arrived within <see cref="ConcentratorBase.LagTime"/> that are ready for processing.</param>
    /// <param name="index">Index of <see cref="IFrame"/> within a second ranging from zero to <c><see cref="ConcentratorBase.FramesPerSecond"/> - 1</c>.</param>
    protected override void PublishFrame(IFrame frame, int index)
    {
        // Increment frame counter
        m_count++;

        if (m_count % AnalysisInterval == 0)
        {
            IDictionary<MeasurementKey, IMeasurement> measurements = frame.Measurements;
            double voltageMagnitude, voltageAngle, currentMagnitude, currentAngle, realPower, reactivePower, deltaT;
            bool warningSignaled = false;

            m_count1 = m_count2;
            m_count2 = m_count;

            if (measurements.TryGetValue(m_voltageMagnitude, out IMeasurement measurement))
                voltageMagnitude = measurement.AdjustedValue;
            else
                return;

            if (measurements.TryGetValue(m_voltageAngle, out measurement))
                voltageAngle = Angle.FromDegrees(measurement.AdjustedValue);
            else
                return;

            if (measurements.TryGetValue(m_currentMagnitude, out measurement))
                currentMagnitude = measurement.AdjustedValue;
            else
                return;

            if (measurements.TryGetValue(m_currentAngle, out measurement))
                currentAngle = Angle.FromDegrees(measurement.AdjustedValue);
            else
                return;

            realPower = 3 * voltageMagnitude * currentMagnitude * Math.Cos(voltageAngle - currentAngle) / SI.Mega;
            reactivePower = 3 * voltageMagnitude * currentMagnitude * Math.Sin(voltageAngle - currentAngle) / SI.Mega;
            deltaT = (m_count2 - m_count1) / (double)FramesPerSecond;

            if (realPower < PSet && reactivePower > QSet)
            {
                m_qAreamVar = m_qAreamVar + deltaT * (reactivePower - QSet);

                if (m_qAreamVar > QAreaSet && voltageMagnitude < VoltageThreshold / SqrtOf3)
                {
                    warningSignaled = true;
                    OutputLOFWarning(realPower, reactivePower, m_qAreamVar);
                }
            }
            else
                m_qAreamVar = 0;

            // Expose output measurement values
            IMeasurement[] outputMeasurements = OutputMeasurements;

            OnNewMeasurements(new IMeasurement[]
            {
                Measurement.Clone(outputMeasurements[(int)Output.WarningSignal], warningSignaled ? 1.0D : 0.0D, frame.Timestamp),
                Measurement.Clone(outputMeasurements[(int)Output.RealPower], realPower, frame.Timestamp),
                Measurement.Clone(outputMeasurements[(int)Output.ReactivePower], reactivePower, frame.Timestamp),
                Measurement.Clone(outputMeasurements[(int)Output.QAreaValue], m_qAreamVar, frame.Timestamp)
            });
        }
    }

    private void OutputLOFWarning(double realPower, double reactivePower, double qAreamVar)
    {
        OnStatusMessage(MessageLevel.Info, $"Loss of Field Detected!\r\n        Real power = {realPower}\r\n    Reactive Power = {reactivePower}\r\n            Q Area = {qAreamVar}\r\n");
    }

    #endregion
}