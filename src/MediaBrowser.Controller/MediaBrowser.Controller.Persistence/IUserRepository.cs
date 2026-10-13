using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Persistence;

public interface IUserRepository : IRepository, IDisposable
{
	void DeleteUser(User user);

	void CreateUser(User user);

	void UpdateUser(User user);

	List<User> RetrieveAllUsers();
}
