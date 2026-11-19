using System;
using System.Threading;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using EmbyPlayedItem = Emby.Server.MediaEncoding.Api.MarkPlayedItem;
using EmbyPlaystateService = Emby.Server.MediaEncoding.Api.PlaystateService;
using EmbyUnplayedItem = Emby.Server.MediaEncoding.Api.MarkUnplayedItem;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Jellyfin 官方客户端使用的无用户 id 路由。除用户数据读写外，全部委托给既有 service 实现。
    /// </summary>
    [Route("/Users/Me", "GET", Summary = "Gets the currently authenticated user")]
    [Authenticated]
    public class GetCurrentUser : IReturn<UserDto>
    {
    }

    [Route("/UserItems/{ItemId}/UserData", "GET", Summary = "Gets user data for an item")]
    [Authenticated]
    public class GetUserItemData : IReturn<UserItemDataDto>
    {
        [ApiMember(Name = "ItemId", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public Guid ItemId { get; set; }
    }

    [Route("/UserItems/{ItemId}/UserData", "POST", Summary = "Updates user data for an item")]
    [Authenticated]
    public class UpdateUserItemData
    {
        // 不要继承 UserItemDataDto：基类已有 string ItemId，这里需要 Guid ItemId 作为路由参数，
        // 属性遮蔽会让 RestPath 的「大小写不敏感属性映射」收到两个 itemid 键并抛
        // ArgumentException: An item with the same key has already been added —— 整个服务起不来。
        [ApiMember(Name = "ItemId", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public Guid ItemId { get; set; }

        public double? Rating { get; set; }

        public bool? Likes { get; set; }

        public long PlaybackPositionTicks { get; set; }

        public int PlayCount { get; set; }

        public bool IsFavorite { get; set; }

        public bool Played { get; set; }

        public DateTime? LastPlayedDate { get; set; }
    }

    [Route("/UserPlayedItems/{ItemId}", "POST", Summary = "Marks an item as played for the current user")]
    [Authenticated]
    public class MarkPlayedItemForCurrentUser : IReturn<UserItemDataDto>
    {
        [ApiMember(Name = "ItemId", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public Guid ItemId { get; set; }

        [ApiMember(Name = "DatePlayed", Description = "The date the item was played (if any). Format = yyyyMMddHHmmss", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string DatePlayed { get; set; }
    }

    [Route("/UserPlayedItems/{ItemId}", "DELETE", Summary = "Marks an item as unplayed for the current user")]
    [Authenticated]
    public class MarkUnplayedItemForCurrentUser : IReturn<UserItemDataDto>
    {
        [ApiMember(Name = "ItemId", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
        public Guid ItemId { get; set; }
    }

    /// <summary>
    /// Jellyfin 官方客户端使用的无用户 id 路由。除用户数据读写外，全部委托给既有 service 实现。
    /// </summary>
    [Authenticated]
    public class JellyfinCompatibilityService : BaseApiService
    {
        private readonly IAuthorizationContext _authContext;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly UserService _userService;
        private readonly EmbyPlaystateService _playstateService;

        public JellyfinCompatibilityService(
            IAuthorizationContext authContext,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            UserService userService,
            EmbyPlaystateService playstateService)
        {
            _authContext = authContext;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _userService = userService;
            _playstateService = playstateService;
        }

        public object Get(GetCurrentUser request)
        {
            var userId = GetCurrentUserId();

            // 直接复用 /Users/{Id} 的实现，保持 UserDto 形状一致。
            ((IRequiresRequest)_userService).Request = Request;
            return _userService.Get(new GetUser { Id = userId });
        }

        public object Get(GetUserItemData request)
        {
            var user = GetCurrentUserEntity();
            var item = GetItem(request.ItemId);

            return ToOptimizedResult(_userDataManager.GetUserDataDto(item, user));
        }

        public object Post(UpdateUserItemData request)
        {
            var user = GetCurrentUserEntity();
            var item = GetItem(request.ItemId);

            var data = _userDataManager.GetUserData(user, item);
            if (request.Rating.HasValue)
            {
                data.Rating = request.Rating.Value;
            }
            else if (request.Likes.HasValue)
            {
                data.Likes = request.Likes.Value;
            }

            data.PlaybackPositionTicks = request.PlaybackPositionTicks;
            data.PlayCount = request.PlayCount;
            data.IsFavorite = request.IsFavorite;
            data.Played = request.Played;

            if (request.LastPlayedDate.HasValue)
            {
                data.LastPlayedDate = request.LastPlayedDate.Value;
            }

            _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);

            return ToOptimizedResult(_userDataManager.GetUserDataDto(item, user));
        }

        public object Post(MarkPlayedItemForCurrentUser request)
        {
            // 与 /Users/{UserId}/PlayedItems/{Id} 共用 PlaystateService 实现。
            ((IRequiresRequest)_playstateService).Request = Request;
            return _playstateService.Post(new EmbyPlayedItem
            {
                UserId = GetCurrentUserId(),
                Id = request.ItemId,
                DatePlayed = request.DatePlayed
            });
        }

        public object Delete(MarkUnplayedItemForCurrentUser request)
        {
            // 与 /Users/{UserId}/PlayedItems/{Id} 共用 PlaystateService 实现。
            ((IRequiresRequest)_playstateService).Request = Request;
            return _playstateService.Delete(new EmbyUnplayedItem
            {
                UserId = GetCurrentUserId(),
                Id = request.ItemId
            });
        }

        private Guid GetCurrentUserId()
        {
            var userId = _authContext.GetAuthorizationInfo(Request).UserId;
            if (userId.Equals(Guid.Empty))
            {
                throw new ResourceNotFoundException("Authenticated user not found");
            }

            return userId;
        }

        private User GetCurrentUserEntity()
        {
            var user = _userManager.GetUserById(GetCurrentUserId());
            if (user == null)
            {
                throw new ResourceNotFoundException("Authenticated user not found");
            }

            return user;
        }

        private BaseItem GetItem(Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                throw new ResourceNotFoundException(string.Format("Item {0} not found", itemId));
            }

            return item;
        }
    }
}
