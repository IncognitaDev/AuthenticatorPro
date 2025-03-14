// Copyright (C) 2022 jmh
// SPDX-License-Identifier: GPL-3.0-only

using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.Widget;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Preference;
using AuthenticatorPro.Droid.Callback;
using AuthenticatorPro.Droid.Fragment;
using AuthenticatorPro.Droid.Preference;
using AuthenticatorPro.Droid.Util;
using Javax.Crypto;
using System;

namespace AuthenticatorPro.Droid.Activity
{
    [Activity]
    internal class SettingsActivity : SensitiveSubActivity, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        private PreferenceWrapper _preferences;
        private SettingsFragment _fragment;
        private bool _shouldRecreateMain;

        public SettingsActivity() : base(Resource.Layout.activitySettings) { }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // If a setting that requires changes to the main activity has changed
            // return a result telling it to recreate.
            _shouldRecreateMain =
                savedInstanceState != null && savedInstanceState.GetBoolean("shouldRecreateMain", false);
            _preferences = new PreferenceWrapper(this);

            var toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            SupportActionBar.SetTitle(Resource.String.settings);
            SupportActionBar.SetDisplayHomeAsUpEnabled(true);
            SupportActionBar.SetDisplayShowHomeEnabled(true);
            SupportActionBar.SetHomeAsUpIndicator(Resource.Drawable.ic_action_arrow_back);

            var prefs = PreferenceManager.GetDefaultSharedPreferences(this);
            prefs.RegisterOnSharedPreferenceChangeListener(this);

            _fragment = new SettingsFragment();
            _fragment.PreferencesCreated += delegate
            {
                UpdateBackupRemindersEnabled(prefs);
                UpdateSecuritySettingsEnabled();
            };

            SupportFragmentManager.BeginTransaction()
                .Replace(Resource.Id.layoutFragment, _fragment)
                .Commit();
        }

        public override void Finish()
        {
            if (_shouldRecreateMain)
            {
                SetResult(Result.Ok, null);
            }

            base.Finish();
        }

        public void OnSharedPreferenceChanged(ISharedPreferences sharedPreferences, string key)
        {
            switch (key)
            {
                case "passwordChanged":
                    _preferences.PasswordChanged = false;
                    UpdateSecuritySettingsEnabled();
                    _shouldRecreateMain = true;
                    break;

                case "pref_theme":
                    UpdateTheme();
                    break;

                case "pref_language":
                case "pref_accentColour":
                    _shouldRecreateMain = true;
                    Recreate();
                    break;

                case "pref_tapToReveal":
                case "pref_viewMode":
                case "pref_codeGroupSize":
                case "pref_transparentStatusBar":
                    _shouldRecreateMain = true;
                    break;

                case "pref_autoBackupEnabled":
                    UpdateBackupRemindersEnabled(sharedPreferences);
                    break;

                case "pref_allowBiometrics":
                    var pref = _fragment.FindPreference(key);
                    if (pref != null)
                    {
                        ((BiometricsPreference) pref).Checked = _preferences.AllowBiometrics;
                    }

                    break;
            }
        }

        public override bool OnSupportNavigateUp()
        {
            Finish();
            return base.OnSupportNavigateUp();
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if (item.ItemId == Android.Resource.Id.Home)
            {
                Finish();
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            base.OnSaveInstanceState(outState);
            outState.PutBoolean("shouldRecreateMain", _shouldRecreateMain);
        }

        public override void OnBackPressed()
        {
            Finish();
            base.OnBackPressed();
        }

        #region Preference states

        private void UpdateBackupRemindersEnabled(ISharedPreferences sharedPreferences)
        {
            var autoBackupEnabled = sharedPreferences.GetBoolean("pref_autoBackupEnabled", false);
            _fragment.FindPreference("pref_showBackupReminders").Enabled = !autoBackupEnabled;
        }

        private void UpdateSecuritySettingsEnabled()
        {
            if (!_preferences.PasswordProtected)
            {
                _fragment.FindPreference("pref_allowBiometrics").Enabled = false;
                _fragment.FindPreference("pref_timeout").Enabled = false;
                _fragment.FindPreference("pref_databasePasswordBackup").Enabled = false;
                return;
            }

            var biometricManager = BiometricManager.From(this);
            var biometricsAvailable =
                biometricManager.CanAuthenticate(BiometricManager.Authenticators.BiometricStrong) ==
                BiometricManager.BiometricSuccess;
            _fragment.FindPreference("pref_allowBiometrics").Enabled = biometricsAvailable;

            _fragment.FindPreference("pref_timeout").Enabled = true;
            _fragment.FindPreference("pref_databasePasswordBackup").Enabled = true;
        }

        #endregion

        #region Biometrics

        public void EnableBiometrics(Action<bool> callback)
        {
            var passwordStorage = new PasswordStorageManager(this);
            var executor = ContextCompat.GetMainExecutor(this);
            var authCallback = new AuthenticationCallback();

            authCallback.Succeeded += async (_, result) =>
            {
                try
                {
                    var password = await SecureStorageWrapper.GetDatabasePassword();
                    passwordStorage.Store(password, result.CryptoObject.Cipher);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                    callback(false);
                    return;
                }

                callback(true);
            };

            authCallback.Failed += delegate
            {
                callback(false);
            };

            authCallback.Errored += delegate
            {
                // Do something, probably
                callback(false);
            };

            var prompt = new BiometricPrompt(this, executor, authCallback);

            var promptInfo = new BiometricPrompt.PromptInfo.Builder()
                .SetTitle(GetString(Resource.String.setupBiometricUnlock))
                .SetNegativeButtonText(GetString(Resource.String.cancel))
                .SetConfirmationRequired(false)
                .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong)
                .Build();

            Cipher cipher;

            try
            {
                cipher = passwordStorage.GetEncryptionCipher();
            }
            catch (Exception e)
            {
                Logger.Error(e);
                passwordStorage.Clear();
                callback(false);
                return;
            }

            prompt.Authenticate(promptInfo, new BiometricPrompt.CryptoObject(cipher));
        }

        public void ClearBiometrics()
        {
            var storage = new PasswordStorageManager(this);
            storage.Clear();
        }

        #endregion
    }
}