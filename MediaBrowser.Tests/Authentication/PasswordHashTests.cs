using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Common.Implementations.Serialization;
using Emby.Server.Implementations.Cryptography;
using Emby.Server.Implementations.EnvironmentInfo;
using Emby.Server.Implementations.IO;
using Emby.Server.Implementations.Library;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Users;
using MediaBrowser.Tests.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Authentication
{
    [TestClass]
    public class PasswordHashTests
    {
        private const string NewPassword = "PBKDF2**";

        [TestMethod]
        public async Task NewPasswordProducesVersionedHash()
        {
            using var fixture = new Fixture();
            var user = await fixture.Manager.CreateUser("newuser").ConfigureAwait(false);

            await fixture.Manager.ChangePassword(user, NewPassword).ConfigureAwait(false);

            StringAssert.StartsWith(user.Password, PasswordHash.Prefix);
            Assert.AreEqual(user.Password, fixture.Repository.GetStoredPassword(user.Id));
            Assert.IsTrue(PasswordHash.TryParse(user.Password, out var parsed));
            Assert.IsFalse(parsed.IsLegacy);
            Assert.AreEqual(PasswordHash.DefaultIterations, parsed.Iterations);
            Assert.IsTrue(parsed.Verify(fixture.Crypto, NewPassword));
        }

        [TestMethod]
        public async Task LegacySha1PasswordCanStillAuthenticate()
        {
            using var fixture = new Fixture();
            var user = await fixture.CreateLegacyUser("legacy-login").ConfigureAwait(false);

            var result = await fixture.Manager.AuthenticateUser("legacy-login", "secret", null, "127.0.0.1", true).ConfigureAwait(false);

            Assert.AreSame(user, result);
            Assert.IsTrue(PasswordHash.TryParse(fixture.Repository.GetStoredPassword(user.Id), out var parsed));
            Assert.IsFalse(parsed.IsLegacy);
        }

        [TestMethod]
        public async Task SuccessfulLegacyLoginIsMigratedAndNewHashVerifies()
        {
            using var fixture = new Fixture();
            var user = await fixture.CreateLegacyUser("legacy-upgrade").ConfigureAwait(false);

            var first = await fixture.Manager.AuthenticateUser("legacy-upgrade", "secret", null, "127.0.0.1", false).ConfigureAwait(false);
            Assert.AreSame(user, first);

            var reloaded = await fixture.Repository.GetUserAsync(user.Id).ConfigureAwait(false);
            StringAssert.StartsWith(reloaded.Password, PasswordHash.Prefix);
            Assert.IsTrue(PasswordHash.TryParse(reloaded.Password, out var parsed));
            Assert.IsFalse(parsed.IsLegacy);
            Assert.IsTrue(parsed.Verify(fixture.Crypto, "secret"));

            var second = await fixture.Manager.AuthenticateUser("legacy-upgrade", "secret", null, "127.0.0.1", true).ConfigureAwait(false);
            Assert.AreSame(user, second);
        }

        [TestMethod]
        public async Task FailedLoginDoesNotChangeStoredHash()
        {
            using var fixture = new Fixture();
            var user = await fixture.CreateLegacyUser("legacy-wrong").ConfigureAwait(false);
            var original = user.Password;

            var result = await fixture.Manager.AuthenticateUser("legacy-wrong", "wrong", null, "127.0.0.1", true).ConfigureAwait(false);

            Assert.IsNull(result);
            Assert.AreEqual(original, fixture.Repository.GetStoredPassword(user.Id));
            Assert.AreEqual(original, user.Password);
            Assert.IsTrue(fixture.Provider.IsLegacyHash(original));

            var versioned = PasswordHash.Create(fixture.Crypto, "secret").ToString();
            user.Password = versioned;
            fixture.Repository.Record(user);
            var failed = await fixture.Manager.AuthenticateUser("legacy-wrong", "wrong", null, "127.0.0.1", true).ConfigureAwait(false);
            Assert.IsNull(failed);
            Assert.AreEqual(versioned, fixture.Repository.GetStoredPassword(user.Id));
        }

        [TestMethod]
        public async Task EmptyPasswordSemanticsArePreserved()
        {
            using var fixture = new Fixture();
            var user = await fixture.Manager.CreateUser("empty-user").ConfigureAwait(false);

            Assert.IsTrue(fixture.Provider.IsEmptyPassword(null));
            Assert.IsTrue(fixture.Provider.IsEmptyPassword(string.Empty));
            Assert.IsFalse(PasswordHash.TryParse(null, out _));
            Assert.IsFalse(PasswordHash.TryParse(string.Empty, out _));
            Assert.IsTrue(fixture.Provider.IsEmptyPassword(fixture.Provider.GetEmptyHashedString(user)));
            Assert.IsFalse(await fixture.Provider.HasPassword(user).ConfigureAwait(false));
            Assert.IsFalse(fixture.Manager.GetUserDto(user).HasConfiguredPassword);

            await fixture.Manager.ChangePassword(user, string.Empty).ConfigureAwait(false);
            StringAssert.StartsWith(user.Password, PasswordHash.Prefix);
            Assert.IsTrue(fixture.Provider.IsEmptyPassword(user.Password));
            Assert.IsFalse(await fixture.Provider.HasPassword(user).ConfigureAwait(false));
            Assert.IsNotNull(await fixture.Manager.AuthenticateUser("empty-user", string.Empty, null, "127.0.0.1", true).ConfigureAwait(false));
            Assert.IsNotNull(await fixture.Manager.AuthenticateUser("empty-user", null, Sha1Hex(string.Empty), "127.0.0.1", true).ConfigureAwait(false));

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => fixture.Manager.ChangePassword(user, null)).ConfigureAwait(false);
        }

        [TestMethod]
        public void SamePasswordProducesDifferentHashes()
        {
            var crypto = new CryptographyProvider();
            var first = PasswordHash.Create(crypto, "same").ToString();
            var second = PasswordHash.Create(crypto, "same").ToString();

            Assert.AreNotEqual(first, second);
            Assert.IsTrue(PasswordHash.TryParse(first, out var parsedFirst));
            Assert.IsTrue(PasswordHash.TryParse(second, out var parsedSecond));
            Assert.AreNotEqual(Convert.ToBase64String(parsedFirst.Salt), Convert.ToBase64String(parsedSecond.Salt));
            Assert.IsTrue(parsedFirst.Verify(crypto, "same"));
            Assert.IsTrue(parsedSecond.Verify(crypto, "same"));
            Assert.IsFalse(parsedFirst.Verify(crypto, "different"));
        }

        [TestMethod]
        public async Task LocalPasswordLoginAlsoMigratesLegacyHash()
        {
            using var fixture = new Fixture(localNetwork: true);
            var user = await fixture.CreateLegacyUser("local-upgrade").ConfigureAwait(false);
            user.Configuration.EnableLocalPassword = true;
            fixture.Manager.ChangeEasyPassword(user, null, Sha1Hex("easy"));

            var result = await fixture.Manager.AuthenticateUser("local-upgrade", "easy", null, "127.0.0.1", false).ConfigureAwait(false);

            Assert.AreSame(user, result);
            StringAssert.StartsWith(user.EasyPassword, PasswordHash.Prefix);
            Assert.IsTrue(fixture.Provider.IsLegacyHash(user.Password));
            Assert.IsTrue(fixture.Provider.Verify(user.EasyPassword, "easy"));
        }

        private static string Sha1Hex(string password)
        {
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        }

        private sealed class Fixture : IDisposable
        {
            public CryptographyProvider Crypto { get; } = new CryptographyProvider();

            public DefaultAuthenticationProvider Provider { get; }

            public UserManager Manager { get; }

            public MemoryUserRepository Repository { get; }

            public Fixture(bool localNetwork = false)
            {
                var logger = new NullLogger();
                var fileSystem = new ManagedFileSystem(logger, new EnvironmentInfo(), ".", System.IO.Path.GetTempPath(), false);
                Repository = new MemoryUserRepository();
                var configurationManager = new FakeConfigManager();
                BaseItem.ConfigurationManager = configurationManager;
                Manager = new UserManager(
                    logger,
                    configurationManager,
                    Repository,
                    new FakeXmlSerializer(),
                    new StubNetworkManager { LocalNetwork = localNetwork },
                    () => null,
                    () => null,
                    new FakeAppHost(),
                    new JsonSerializer(fileSystem, logger),
                    fileSystem,
                    Crypto);
                Provider = new DefaultAuthenticationProvider(Crypto);
                Manager.AddParts(new IAuthenticationProvider[] { Provider });
                User.UserManager = Manager;
            }

            public async Task<User> CreateLegacyUser(string name)
            {
                var user = await Manager.CreateUser(name).ConfigureAwait(false);
                user.Password = Sha1Hex("secret");
                user.Policy = new UserPolicy
                {
                    EnableRemoteAccess = true,
                    AccessSchedules = Array.Empty<AccessSchedule>()
                };
                user.Configuration = new UserConfiguration();
                Repository.Record(user);
                return user;
            }

            public void Dispose()
            {
                Repository.Dispose();
            }
        }

        private sealed class MemoryUserRepository : IUserRepository
        {
            private readonly Dictionary<Guid, User> _users = new Dictionary<Guid, User>();
            private readonly Dictionary<Guid, string> _storedPasswords = new Dictionary<Guid, string>();

            public string Name => "TestMemory";

            public string GetStoredPassword(Guid id)
            {
                return _storedPasswords.TryGetValue(id, out var value) ? value : null;
            }

            public void Record(User user)
            {
                _users[user.Id] = user;
                _storedPasswords[user.Id] = user.Password;
            }

            public Task CreateUserAsync(User user, CancellationToken cancellationToken = default)
            {
                Record(user);
                return Task.CompletedTask;
            }

            public Task UpdateUserAsync(User user, CancellationToken cancellationToken = default)
            {
                Record(user);
                return Task.CompletedTask;
            }

            public Task<List<User>> RetrieveAllUsersAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_users.Values.ToList());
            }

            public Task DeleteUserAsync(User user, CancellationToken cancellationToken = default)
            {
                _users.Remove(user.Id);
                _storedPasswords.Remove(user.Id);
                return Task.CompletedTask;
            }

            public Task<User> GetUserAsync(Guid guid, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_users.TryGetValue(guid, out var user) ? user : null);
            }

            public Task<long?> GetInternalIdAsync(Guid guid, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<long?>(null);
            }

            public void Dispose()
            {
            }
        }

        private sealed class StubNetworkManager : INetworkManager
        {
            public bool LocalNetwork { get; set; }

            public Func<string[]> LocalSubnetsFn { get; set; }

            public event EventHandler NetworkChanged
            {
                add { }
                remove { }
            }

            public int GetRandomUnusedTcpPort() => 0;

            public int GetRandomUnusedUdpPort() => 0;

            public List<string> GetMacAddresses() => new List<string>();

            public bool IsInPrivateAddressSpace(string endpoint) => false;

            public IEnumerable<NetworkShare> GetNetworkShares(string path) => Array.Empty<NetworkShare>();

            public IEnumerable<FileSystemEntryInfo> GetNetworkDevices() => Array.Empty<FileSystemEntryInfo>();

            public bool IsInLocalNetwork(string endpoint) => LocalNetwork;

            public IpAddressInfo[] GetLocalIpAddresses() => Array.Empty<IpAddressInfo>();

            public IpAddressInfo ParseIpAddress(string ipAddress) => null;

            public bool TryParseIpAddress(string ipAddress, out IpAddressInfo ipAddressInfo)
            {
                ipAddressInfo = null;
                return false;
            }

            public Task<IpAddressInfo[]> GetHostAddressesAsync(string host) => Task.FromResult(Array.Empty<IpAddressInfo>());

            public bool IsAddressInSubnets(string addressString, string[] subnets) => false;
        }
    }
}
