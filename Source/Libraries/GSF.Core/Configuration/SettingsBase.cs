﻿//******************************************************************************************************
//  SettingsBase.cs - Gbtc
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
//  01/30/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  03/31/2009 - J. Ritchie Carroll
//       Added code to allow override of name used to serialize field or property to configuration file
//          by applying a SettingNameAttribute to the member.
//  04/01/2009 - J. Ritchie Carroll
//       Added code to optionally encrypt settings based on EncryptSettingAttribute and to pickup
//          DefaultValueAttribute value if provided and current value was uninitialized.
//  08/05/2009 - Josh L. Patterson
//       Edited Comments.
//  09/14/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  12/05/2010 - Pinal C. Patel
//       Added Culture property that can be used for specifying a culture to use for value conversion
//       and updated all value conversions to use the specified culture.
//  01/04/2011 - J. Ritchie Carroll
//       Modified culture to default to InvariantCulture for English style parsing defaults.
//  12/14/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GSF.Reflection;
using GSF.Security.Cryptography;

namespace GSF.Configuration
{
    /// <summary>
    /// Represents the base class for application settings that are synchronized with its configuration file.
    /// </summary>
    /// <remarks>
    /// In order to make custom types serializable for the configuration file, implement a <see cref="TypeConverter"/> for the type.<br/>
    /// See <a href="http://msdn.microsoft.com/en-us/library/ayybcxe5.aspx">MSDN</a> for details.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class SettingsBase : IDisposable, IEnumerable<string>
    {
        #region [ Members ]

        // Fields
        private CultureInfo m_culture;
        private BindingFlags m_memberAccessBindingFlags;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="SettingsBase"/> class for the application's configuration file.
        /// </summary>
        /// <param name="requireSerializeSettingAttribute">
        /// Assigns flag that determines if <see cref="SerializeSettingAttribute"/> is required
        /// to exist before a field or property is serialized to the configuration file.
        /// </param>
        protected SettingsBase(bool requireSerializeSettingAttribute)
        {
            m_culture = CultureInfo.InvariantCulture;
            RequireSerializeSettingAttribute = requireSerializeSettingAttribute;
            m_memberAccessBindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        }

        /// <summary>
        /// Releases the unmanaged resources before the <see cref="CategorizedSettingsBase"/> object is reclaimed by <see cref="GC"/>.
        /// </summary>
        ~SettingsBase()
        {
            // If user failed to dispose class, we make sure settings get saved...
            Dispose(false);
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the <see cref="CultureInfo"/> to use for the conversion of setting values to and from <see cref="string"/>.
        /// </summary>
        [Browsable(false), SerializeSetting(false)]
        public CultureInfo Culture
        {
            get => m_culture;
            set => m_culture = value ?? CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Gets or sets <see cref="BindingFlags"/> used to access fields and properties of derived class.
        /// </summary>
        /// <remarks>
        /// Value defaults to <c><see cref="BindingFlags.Public"/> | <see cref="BindingFlags.Instance"/> | <see cref="BindingFlags.DeclaredOnly"/></c>.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected virtual BindingFlags MemberAccessBindingFlags
        {
            get => m_memberAccessBindingFlags;
            set => m_memberAccessBindingFlags = value;
        }

        /// <summary>
        /// Gets or sets flag that determines if <see cref="SerializeSettingAttribute"/> is
        /// required to exist before a field or property is serialized to the configuration
        /// file; defaults to False.
        /// </summary>
        [Browsable(false), SerializeSetting(false)]
        public bool RequireSerializeSettingAttribute { get; set; }

        /// <summary>
        /// Gets or sets the value of the specified field or property.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns>Value of setting.</returns>
        /// <remarks>This is the default member of this class.</remarks>
        public string this[string name]
        {
            get => GetValue<string>(name);
            set => SetValue(name, value);
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases all the resources used by the <see cref="CategorizedSettingsBase"/> object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="CategorizedSettingsBase"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (m_disposed)
                return;

            try
            {
                // We'll make sure settings are saved when class is properly disposed...
                if (disposing)
                    Save();
            }
            finally
            {
                m_disposed = true;  // Prevent duplicate dispose.
            }
        }

        /// <summary>
        /// Implementor should create setting in configuration file (or other location).
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <param name="value">Setting value.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected abstract void CreateSetting(string name, string setting, string value);

        /// <summary>
        /// Implementor should retrieve setting from configuration file (or other location).
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <returns>Setting value.</returns>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected abstract string RetrieveSetting(string name, string setting);

        /// <summary>
        /// Implementor should store setting to configuration file (or other location).
        /// </summary>
        /// <param name="name">Field or property name, if useful (can be different from setting name).</param>
        /// <param name="setting">Setting name.</param>
        /// <param name="value">Setting value.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected abstract void StoreSetting(string name, string setting, string value);

        /// <summary>
        /// Implementor should persist any pending changes to configuration file (or other location).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected abstract void PersistSettings();

        /// <summary>
        /// Gets setting name to use for specified field or property. 
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns><see cref="SettingNameAttribute.Name"/> applied to specified field or property; or <paramref name="name"/> if attribute does not exist.</returns>
        /// <remarks>
        /// Field or property name will be used for setting name unless user applied a <see cref="SettingNameAttribute"/>
        /// on the field or property to override name used to serialize value in configuration file.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="name"/> cannot be null or empty.</exception>
        public string GetSettingName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"{nameof(name)} cannot be null or empty", nameof(name));

            return GetAttributeValue<SettingNameAttribute, string>(name, name, attribute => attribute.Name).NotEmpty(name);
        }

        /// <summary>
        /// Gets the default value specified by <see cref="DefaultValueAttribute"/>, if any, applied to the specified field or property. 
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns>Default value applied to specified field or property; or null if one does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> cannot be null or empty.</exception>
        public object GetDefaultValue(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"{nameof(name)} cannot be null or empty", nameof(name));

            return GetAttributeValue<DefaultValueAttribute, object>(name, null, attribute => attribute.Value);
        }

        /// <summary>
        /// Gets the encryption status specified by <see cref="EncryptSettingAttribute"/>, if any, applied to the specified field or property. 
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns>Encryption status applied to specified field or property; or <c>false</c> if one does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> cannot be null or empty.</exception>
        public bool GetEncryptStatus(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"{nameof(name)} cannot be null or empty", nameof(name));

            return GetAttributeValue<EncryptSettingAttribute, bool>(name, false, attribute => attribute.Encrypt);
        }

        /// <summary>
        /// Gets the optional private encryption key specified by <see cref="EncryptSettingAttribute"/>, if any, applied to the specified field or property. 
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <returns>Encryption private key applied to specified field or property; or <c>null</c> if one does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> cannot be null or empty.</exception>
        public string GetEncryptKey(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"{nameof(name)} cannot be null or empty", nameof(name));

            return GetAttributeValue<EncryptSettingAttribute, string>(name, null, attribute => attribute.PrivateKey).NotEmpty(name);
        }

        /// <summary>
        /// Adds a setting to the application's configuration file, if it doesn't already exist.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <param name="value">Setting value.</param>
        /// <remarks>
        /// Use this function to ensure a setting exists, it will not override an existing value.
        /// </remarks>
        public void CreateValue(string name, object value)
        {
            string setting = GetSettingName(name);

            if (value is null)
                CreateSetting(name, setting, EncryptValue(name, ""));
            else
                CreateSetting(name, setting, EncryptValue(name, Common.TypeConvertToString(value, m_culture)));
        }

        /// <summary>
        /// Copies the given value into the specified application setting.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <param name="value">Setting value.</param>
        public void SetValue(string name, object value)
        {
            string setting = GetSettingName(name);

            if (value is null)
                StoreSetting(name, setting, EncryptValue(name, ""));
            else
                StoreSetting(name, setting, EncryptValue(name, Common.TypeConvertToString(value, m_culture)));
        }

        /// <summary>
        /// Gets the application's configuration file setting converted to the given type.
        /// </summary>
        /// <typeparam name="T">Type to use for setting conversion.</typeparam>
        /// <param name="name">Field or property name.</param>
        /// <returns>Value of specified configuration file setting converted to the given type.</returns>
        public T GetValue<T>(string name)
        {
            string setting = GetSettingName(name);

            return DecryptValue(name, RetrieveSetting(name, setting)).ConvertToType<T>(m_culture);
        }

        /// <summary>
        /// Gets the application's configuration file setting converted to the given type.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <param name="type">Setting type.</param>
        /// <returns>Value of specified configuration file setting converted to the given type.</returns>
        public object GetValue(string name, Type type)
        {
            string setting = GetSettingName(name);

            return DecryptValue(name, RetrieveSetting(name, setting)).ConvertToType(type, m_culture);
        }

        /// <summary>
        /// Copies the specified application setting into the given value.
        /// </summary>
        /// <typeparam name="T">Type to use for setting conversion.</typeparam>
        /// <param name="name">Field or property name.</param>
        /// <param name="value">Setting value.</param>
        public void GetValue<T>(string name, out T value)
        {
            string setting = GetSettingName(name);

            value = DecryptValue(name, RetrieveSetting(name, setting)).ConvertToType<T>(m_culture);
        }

        // Encrypt setting value and return a base64 encoded value
        private string EncryptValue(string name, string value) =>
            // If encrypt attribute has been applied, encrypt value
            GetEncryptStatus(name) ? 
                value.Encrypt(GetEncryptKey(name), CipherStrength.Aes256) : 
                value;

        // Decrypt setting value
        private string DecryptValue(string name, string value) =>
            // If encrypt attribute has been applied, decrypt value
            GetEncryptStatus(name) ? 
                value.Decrypt(GetEncryptKey(name), CipherStrength.Aes256) : 
                value;

        /// <summary>
        /// Initializes configuration settings from derived class fields or properties.
        /// </summary>
        protected virtual void Initialize()
        {
            // Make sure all desired settings exist initialized with default values. Settings are
            // assumed to be public fields or public properties in derived class - so we enumerate
            // through of these making sure a setting exists for each field and property

            // Verify a configuration setting exists for each field
            ExecuteActionForFields(field => CreateValue(field.Name, DeriveDefaultValue(field.Name, field.GetValue(this))));

            // Verify a configuration setting exists for each property
            ExecuteActionForProperties(property => CreateValue(property.Name, DeriveDefaultValue(property.Name, property.GetValue(this, null))), BindingFlags.GetProperty);

            // If any new values were encountered, make sure they are flushed to config file
            PersistSettings();

            // Load current settings
            Load();
        }

        /// <summary>
        /// Restores the default settings of the configuration file.
        /// </summary>
        public virtual void RestoreDefaultSettings()
        {
            // Restore each field to its default value
            ExecuteActionForFields(field => SetValue(field.Name, GetDefaultValue(field.Name)));

            // Restore each property to its default value
            ExecuteActionForProperties(property => SetValue(property.Name, GetDefaultValue(property.Name)), BindingFlags.GetProperty);

            // If any values were changed, make sure they are flushed to config file
            PersistSettings();

            // Load current settings
            Load();
        }

        /// <summary>
        /// Attempts to get best default value for given member.
        /// </summary>
        /// <param name="name">Field or property name.</param>
        /// <param name="value">Current field or property value.</param>
        /// <remarks>
        /// If <paramref name="value"/> is equal to its default(type) value, then any value derived from <see cref="DefaultValueAttribute"/> will be used instead.
        /// </remarks>
        /// <returns>The object that is the best default value.</returns>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected object DeriveDefaultValue(string name, object value)
        {
            // See if value is equal to its default value (i.e., uninitialized)
            if (!Common.IsDefaultValue(value))
                return value;

            // See if any value exists in a DefaultValueAttribute
            object defaultValue = GetDefaultValue(name);
            return defaultValue ?? value;
        }

        /// <summary>
        /// Returns an enumerator based on <see cref="String"/> elements that iterates over the field and property names of this class
        /// that are targeted for serialization to the configuration file.
        /// </summary>
        /// <returns>An <see cref="IEnumerator"/> object that can be used to iterate through the collection.</returns>
        public IEnumerator<string> GetEnumerator()
        {
            List<string> members = new();

            // Get names of fields
            ExecuteActionForFields(field => members.Add(field.Name));

            // Get names of properties
            ExecuteActionForProperties(property => members.Add(property.Name), BindingFlags.GetProperty);

            return members.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Loads configuration file into setting fields.
        /// </summary>
        public virtual void Load()
        {
            // Load configuration file settings into fields
            ExecuteActionForFields(field => field.SetValue(this, GetValue(field.Name, field.FieldType)));

            // Load configuration file settings into properties
            ExecuteActionForProperties(property => property.SetValue(this, GetValue(property.Name, property.PropertyType), null), BindingFlags.SetProperty);
        }

        /// <summary>
        /// Saves setting fields into configuration file.
        /// </summary>
        public virtual void Save()
        {
            // Saves setting fields into configuration file values
            ExecuteActionForFields(field => SetValue(field.Name, field.GetValue(this)));

            // Saves setting properties into configuration file values
            ExecuteActionForProperties(property => SetValue(property.Name, property.GetValue(this, null)), BindingFlags.GetProperty);

            // Make sure any changes are flushed to config file
            PersistSettings();
        }

        /// <summary>
        /// Executes specified action over all public derived class member fields.
        /// </summary>
        /// <param name="fieldAction">Action to execute for all derived class member fields.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected void ExecuteActionForFields(Action<FieldInfo> fieldAction) => 
            ExecuteActionForMembers(fieldAction, GetType().GetFields(m_memberAccessBindingFlags));

        /// <summary>
        /// Executes specified action over all public derived class properties.
        /// </summary>
        /// <param name="propertyAction">Action to execute for all properties.</param>
        /// <param name="isGetOrSet"><see cref="BindingFlags.GetProperty"/> or <see cref="BindingFlags.SetProperty"/>.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected void ExecuteActionForProperties(Action<PropertyInfo> propertyAction, BindingFlags isGetOrSet)
        {
            bool get = isGetOrSet.HasFlag(BindingFlags.GetProperty);
            bool set = isGetOrSet.HasFlag(BindingFlags.SetProperty);

            void memberAction(PropertyInfo property)
            {
                if (property.GetIndexParameters().Length == 0)
                    propertyAction(property);
            }

            PropertyInfo[] properties = GetType()
                .GetProperties(m_memberAccessBindingFlags)
                .Where(property => (!get || property.CanRead) && (!set || property.CanWrite))
                .ToArray();

            // Make sure only non-indexer properties are used for settings
            ExecuteActionForMembers(memberAction, properties);
        }

        // Execute specified action over specified members
        private void ExecuteActionForMembers<T>(Action<T> memberAction, T[] members) where T : MemberInfo
        {
            // Execute action for each member
            foreach (T member in members)
            {
                // See if serialize setting attribute exists
                if (member.TryGetAttribute(out SerializeSettingAttribute attribute))
                {
                    // Found serialize setting attribute, perform action if setting is true
                    if (attribute.Serialize)
                        memberAction(member);
                }
                else if (!RequireSerializeSettingAttribute)
                {
                    // Didn't find serialize setting attribute and it's not required, so we perform action
                    memberAction(member);
                }
            }
        }

        /// <summary>
        /// Attempts to find specified attribute and return specified value.
        /// </summary>
        /// <typeparam name="TAttribute">Type of <see cref="Attribute"/> to find.</typeparam>
        /// <typeparam name="TValue">Type of value attribute delegate returns.</typeparam>
        /// <param name="name">Name of field or property to search for attribute.</param>
        /// <param name="defaultValue">Default value to return if attribute doesn't exist.</param>
        /// <param name="attributeValue">Function delegate used to return desired attribute property.</param>
        /// <returns>Specified attribute value if it exists; otherwise default value.</returns>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected TValue GetAttributeValue<TAttribute, TValue>(string name, TValue defaultValue, Func<TAttribute, TValue> attributeValue) where TAttribute : Attribute
        {
            TAttribute attribute;

            // See if field exists with specified name
            FieldInfo field = GetType().GetField(name, m_memberAccessBindingFlags);

            if (field is not null)
            {
                // See if attribute exists on field
                return field.TryGetAttribute(out attribute) ?
                    // Return value as specified by delegate
                    attributeValue(attribute) :
                    // Attribute wasn't found, return default value
                    defaultValue;
            }

            // See if property exists with specified name
            PropertyInfo property = GetType().GetProperty(name, m_memberAccessBindingFlags);

            if (property is not null)
            {
                // See if attribute exists on property
                return property.TryGetAttribute(out attribute) ?
                    // Return value as specified by delegate
                    attributeValue(attribute) :
                    // Attribute wasn't found, return default value
                    defaultValue;
            }

            // Return default value
            return defaultValue;
        }

        #endregion
    }
}