using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Library;

public interface IUserManager
{
	IEnumerable<User> Users { get; }

	event EventHandler<GenericEventArgs<User>> UserUpdated;

	event EventHandler<GenericEventArgs<User>> UserDeleted;

	event EventHandler<GenericEventArgs<User>> UserCreated;

	event EventHandler<GenericEventArgs<User>> UserPolicyUpdated;

	event EventHandler<GenericEventArgs<User>> UserConfigurationUpdated;

	event EventHandler<GenericEventArgs<User>> UserPasswordChanged;

	event EventHandler<GenericEventArgs<User>> UserLockedOut;

	User GetUserById(Guid id);

	User GetUserById(string id);

	User GetUserByName(string name);

	Task RefreshUsersMetadata(CancellationToken cancellationToken);

	Task RenameUser(User user, string newName);

	void UpdateUser(User user);

	Task<User> CreateUser(string name);

	Task DeleteUser(User user);

	Task ResetPassword(User user);

	UserDto GetOfflineUserDto(User user);

	void ResetEasyPassword(User user);

	Task ChangePassword(User user, string newPassword);

	void ChangeEasyPassword(User user, string newPassword, string newPasswordSha1);

	UserDto GetUserDto(User user, string remoteEndPoint = null);

	Task<User> AuthenticateUser(string username, string password, string passwordSha1, string remoteEndPoint, bool isUserSession);

	Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork);

	Task<PinRedeemResult> RedeemPasswordResetPin(string pin);

	UserPolicy GetUserPolicy(User user);

	UserConfiguration GetUserConfiguration(User user);

	void UpdateConfiguration(Guid userId, UserConfiguration newConfiguration);

	void UpdateConfiguration(User user, UserConfiguration newConfiguration);

	void UpdateUserPolicy(Guid userId, UserPolicy userPolicy);

	string MakeValidUsername(string username);

	void AddParts(IEnumerable<IAuthenticationProvider> authenticationProviders);

	NameIdPair[] GetAuthenticationProviders();
}
