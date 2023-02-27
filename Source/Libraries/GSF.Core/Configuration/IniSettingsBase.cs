﻿//******************************************************************************************************
//  IniSettingsBase.cs - Gbtc
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
//  04/01/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  09/14/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  09/21/2011 - J. Ritchie Carroll
//       Excluded class from Mono deployments due to P/Invoke requirements.
//  12/14/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.ComponentModel;
using GSF.Interop;

// ReSharper disable VirtualMemberCallInConstructor
namespace GSF.Configuration
{
    /// <summary>
    /// Represents the base class for application settings that are synchronized to an INI file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In order to make custom types serializable for the INI file, implement a <see cref="TypeConverter"/> for the type.<br/>
    /// See <a href="http://msdn.microsoft.com/en-us/library/ayybcxe5.aspx">MSDN</a> for details.
    /// </para>
    /// <example>
    /// Here is an example class derived from <see cref="IniSettingsBase"/> that automatically
    /// serializes its fields and properties to the INI file.
    /// <code>
    ///    public enum MyEnum
    ///     {
    ///         One,
    ///         Two,
    ///         Three
    ///     }
    /// 
    ///     public class MySettings : IniSettingsBase
    ///     {
    ///         // Private property fields (private fields will not be serialized)
    ///         private double m_doubleVal;
    /// 
    ///         // Public settings fields
    ///         public bool BoolVal = true;
    ///         public int IntVal = 1;
    ///         public float FloatVal = 3.14F;
    ///         public string StrVal = "This is a test...";
    ///         public MyEnum EnumVal = MyEnum.Three;
    ///         
    ///         [SettingName("UserOptions"), EncryptSetting()]
    ///         public string Password = "default";
    /// 
    ///         // Mark this field to not be serialized to INI file...
    ///         [SerializeSetting(false)]
    ///         public decimal DecimalVal;
    /// 
    ///         public MySettings()
    ///             : base(FilePath.GetAbsolutePath("MySettings.ini"), "GeneralSettings") {}
    /// 
    ///         [Category("OtherSettings"), DefaultValue(1.159D)]
    ///         public double DoubleVal
    ///         {
    ///             get
    ///             {
    ///                 return m_doubleVal;
    ///             }
    ///             set
    ///             {
    ///                 m_doubleVal = value;
    ///             }
    ///         }
    /// 
    ///         [SerializeSetting(false)]
    ///         public bool DontSerializeMe { get; set; }
    ///     }
    /// </code>
    /// </example>
    /// </remarks>
    public abstract class IniSettingsBase : SettingsBase
    {
        #region [ Members ]

        // Fields
        private IniFile m_iniFile;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="IniSettingsBase"/> class for the application's INI file.
        /// </summary>
        /// <param name="iniFileName">Name of INI file to use for accessing settings.</param>
        /// <param name="sectionName">Name of default section to use to get and set settings from INI file.</param>
        protected IniSettingsBase(string iniFileName, string sectionName)
            : this(new IniFile(iniFileName), sectionName, true, false, true)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IniSettingsBase"/> class for the application's INI file.
        /// </summary>
        /// <param name="iniFileName">Name of INI file to use for accessing settings.</param>
        /// <param name="sectionName">Name of default section to use to get and set settings from INI file.</param>
        /// <param name="useCategoryAttributes">Determines if category attributes will be used for section names.</param>
        /// <param name="requireSerializeSettingAttribute">
        /// Assigns flag that determines if <see cref="SerializeSettingAttribute"/> is required
        /// to exist before a field or property is serialized to the INI file.
        /// </param>
        /// <remarks>
        /// If <paramref name="useCategoryAttributes"/> is false, all settings will be placed in section labeled by the
        /// <paramref name="sectionName"/> value; otherwise, if a <see cref="CategoryAttribute"/> exists on a field or
        /// property then the member value will serialized into the INI file in a section labeled the same
        /// as the <see cref="CategoryAttribute.Category"/> value and if the attribute doesn't exist the member value
        /// will serialized into the section labeled by the <paramref name="sectionName"/> value.
        /// </remarks>
        protected IniSettingsBase(string iniFileName, string sectionName, bool useCategoryAttributes, bool requireSerializeSettingAttribute)
            : this(new IniFile(iniFileName), sectionName, useCategoryAttributes, requireSerializeSettingAttribute, true)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IniSettingsBase"/> class for the application's INI file.
        /// </summary>
        /// <param name="iniFile">INI file to use for accessing settings.</param>
        /// <param name="sectionName">Name of default section to use to get and set settings from INI file.</param>
        /// <param name="useCategoryAttributes">Determines if category attributes will be used for section names.</param>
        /// <param name="requireSerializeSettingAttribute">
        /// Assigns flag that determines if <see cref="SerializeSettingAttribute"/> is required
        /// to exist before a field or property is serialized to the INI file.
        /// </param>
        /// <param name="initialize">Determines if <see cref="SettingsBase.Initialize"/> method should be called from constructor.</param>
        /// <remarks>
        /// <para>
        /// If <paramref name="useCategoryAttributes"/> is false, all settings will be placed in section labeled by the
        /// <paramref name="sectionName"/> value; otherwise, if a <see cref="CategoryAttribute"/> exists on a field or
        /// property then the member value will serialized into the INI file in a section labeled the same
        /// as the <see cref="CategoryAttribute.Category"/> value and if the attribute doesn't exist the member value
        /// will serialized into the section labeled by the <paramref name="sectionName"/> value.
        /// </para>
        /// <para>
        /// Note that some .NET languages (e.g., Visual Basic) will not initialize member elements before call to constuctor,
        /// in this case <paramref name="initialize"/> should be set to <c>false</c>, then the <see cref="SettingsBase.Initialize"/>
        /// method should be called manually after all properties have been initialized. Alternately, consider using the
        /// <see cref="DefaultValueAttribute"/> on the fields or properties and this will be used to initialize the values.
        /// </para>
        /// </remarks>
        protected IniSettingsBase(IniFile iniFile, string sectionName, bool useCategoryAttributes, bool requireSerializeSettingAttribute, bool initialize)
            : base(requireSerializeSettingAttribute)
        {
            m_iniFile = iniFile;
            SectionName = sectionName;
            UseCategoryAttributes = useCategoryAttributes;

            // Make sure settings exist and load current values
            if (initialize)
                Initialize();
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets reference to working INI file.
        /// </summary>
        /// <exception cref="NullReferenceException">value cannot be null.</exception>
        protected IniFile IniFile
        {
            get => m_iniFile;
            set => m_iniFile = value ?? throw new NullReferenceException("value cannot be null");
        }

        /// <summary>
        /// Gets or sets default name of section used to access settings in INI file.
        /// </summary>
        public string SectionName { get; set; }

        /// <summary>
        /// Gets or sets value that determines whether a <see cref="CategoryAttribute"/> applied to a field or property
        /// will be used for the section name.
        /// </summary>
        /// <remarks>
        /// If <see cref="UseCategoryAttributes"/> is false, all settings will be placed in section labeled by the
        /// <see cref="SectionName"/> value; otherwise, if a <see cref="CategoryAttribute"/> exists on a field or
        /// property then the member value will serialized into the INI file in a section labeled the same
        /// as the <see cref="CategoryAttribute.Category"/> value and if the attribute doesn't exist the member value
        /// will serialized into the section labeled by the <see cref="SectionName"/> value.
        /// </remarks>
        public bool UseCategoryAttributes { get; set; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Create setting in INI file if it doesn't already exist.
        /// This method is for internal use.
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <param name="value">Setting value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void CreateSetting(string name, string setting, string value)
        {
            string section = GetFieldSectionName(name);

            if (string.IsNullOrEmpty(m_iniFile[section, setting]))
                m_iniFile[section, setting] = value;
        }

        /// <summary>
        /// Retrieves setting from INI file.
        /// This method is for internal use.
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <returns>Setting value.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override string RetrieveSetting(string name, string setting) => 
            m_iniFile[GetFieldSectionName(name), setting];

        /// <summary>
        /// Stores setting to INI file.
        /// This method is for internal use.
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <param name="value">Setting value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void StoreSetting(string name, string setting, string value) => 
            m_iniFile[GetFieldSectionName(name), setting] = value;

        /// <summary>
        /// Persists any pending changes to INI file.
        /// This method is for internal use.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void PersistSettings()
        {
            // INI files flush at every update...
        }

        /// <summary>
        /// Gets the section name to use for the specified field or property.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns><see cref="CategoryAttribute.Category"/> applied to specified field or property; or <see cref="SectionName"/> if attribute does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> cannot be null or empty.</exception>
        /// <remarks>
        /// <see cref="CategoryAttribute.Category"/> will only be returned if <see cref="UseCategoryAttributes"/> is <c>true</c>; otherwise
        /// <see cref="SectionName"/> value will be returned.
        /// </remarks>
        public string GetFieldSectionName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name cannot be null or empty");

            // If user wants to respect category attributes, we attempt to use those as configuration section names
            if (UseCategoryAttributes)
                return GetAttributeValue<CategoryAttribute, string>(name, SectionName, attribute => attribute.Category);

            // Otherwise return default category name
            return SectionName;
        }

        #endregion
    }
}