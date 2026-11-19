using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Cryptography;

namespace Emby.Server.Implementations.Library
{
    public class DefaultAuthenticationProvider : IAuthenticationProvider, IRequiresResolvedUser
    {
        private readonly ICryptoProvider _cryptographyProvider;
        public DefaultAuthenticationProvider(ICryptoProvider crypto)
        {
            _cryptographyProvider = crypto;
        }

        public string Name => "Default";

        public bool IsEnabled => true;

        public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        {
            throw new NotImplementedException();
        }

        public Task<ProviderAuthenticationResult> Authenticate(string username, string password, User resolvedUser)
        {
            if (resolvedUser == null)
            {
                throw new Exception("Invalid username or password");
            }

            if (!VerifyPassword(resolvedUser, password))
            {
                throw new Exception("Invalid username or password");
            }

            return Task.FromResult(new ProviderAuthenticationResult
            {
                Username = username
            });
        }

        public Task<bool> HasPassword(User user)
        {
            var hasConfiguredPassword = !IsPasswordEmpty(user, GetPasswordHash(user));
            return Task.FromResult(hasConfiguredPassword);
        }

        private bool IsPasswordEmpty(User user, string passwordHash)
        {
            return IsEmptyPassword(passwordHash);
        }

        public Task ChangePassword(User user, string newPassword)
        {
            string newPasswordHash = null;

            if (newPassword != null)
            {
                newPasswordHash = GetHashedString(user, newPassword);
            }

            if (string.IsNullOrWhiteSpace(newPasswordHash))
            {
                throw new ArgumentNullException("newPasswordHash");
            }

            user.Password = newPasswordHash;

            return Task.CompletedTask;
        }

        public string GetPasswordHash(User user)
        {
            return string.IsNullOrEmpty(user.Password)
                ? GetEmptyHashedString(user)
                : user.Password;
        }

        public string GetEmptyHashedString(User user)
        {
            return GetHashedString(user, string.Empty);
        }

        /// <summary>
        /// Gets a versioned PBKDF2-SHA512 hash of the supplied plaintext.
        /// </summary>
        public string GetHashedString(User user, string str)
        {
            return PasswordHash.Create(_cryptographyProvider, str).ToString();
        }

        /// <summary>
        /// Verifies a plaintext credential against either a versioned or a legacy stored hash.
        /// </summary>
        public bool Verify(string storedHash, string password)
        {
            return PasswordHash.TryParse(storedHash, out var parsed) && parsed.Verify(_cryptographyProvider, password);
        }

        public bool VerifyPassword(User user, string password)
        {
            return Verify(GetPasswordHash(user), password);
        }

        /// <summary>
        /// Verifies the SHA1 hex string sent by legacy clients that do not send the plaintext.
        /// </summary>
        public bool VerifyHashString(string storedHash, string hashString)
        {
            return PasswordHash.TryParse(storedHash, out var parsed) && parsed.VerifyHashString(_cryptographyProvider, hashString);
        }

        public bool IsEmptyPassword(string storedHash)
        {
            return PasswordHash.IsEmpty(_cryptographyProvider, storedHash);
        }

        public bool IsLegacyHash(string storedHash)
        {
            return PasswordHash.TryParse(storedHash, out var parsed) && parsed.IsLegacy;
        }

        /// <summary>
        /// Re-hashes a verified plaintext credential with PBKDF2 (lazy migration).
        /// </summary>
        public void UpgradePassword(User user, string password)
        {
            user.Password = GetHashedString(user, password);
        }
    }
}
