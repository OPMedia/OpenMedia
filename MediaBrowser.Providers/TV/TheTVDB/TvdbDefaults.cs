namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// TheTVDB 的编译期默认凭据。
    ///
    /// 为什么要有这个：TMDB / FanArt / AudioDB 在本仓库里都是把 key 直接写在源码里的，
    /// TVDB 之前是唯一需要用户自己去申请的那个 —— 开箱不可用。
    /// 这里补一个默认值槽位，让「内置」和「用户覆盖」两条路都成立。
    ///
    /// 优先级：<see cref="MediaBrowser.Model.Configuration.TheTvdbConfiguration.ApiKey"/>（配置）
    ///        → 这里的 <see cref="ApiKey"/>（内置）→ 都没有则 provider 明确禁用。
    ///
    /// ⚠️ 把 key 填在这里等于公开发布：本仓库是公开的，**任何拿到代码的人都能用你的 key**，
    /// 而且 TVDB 的配额是按 key 算的。接受这一点再填；不接受就让用户自己在配置里填。
    /// </summary>
    internal static class TvdbDefaults
    {
        /// <summary>TheTVDB v4 API key（thetvdb.com 后台创建）。留空表示不内置。</summary>
        internal const string ApiKey = "";

        /// <summary>可选的 subscriber PIN。</summary>
        internal const string Pin = "";
    }
}
