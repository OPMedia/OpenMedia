# 通知（SPEC/notify）

> 决策：进 v1（用户翻案砍掉了原"排 v2"）。边界：**站内通知中心 + 邮件渠道**，
> Webhook/其他渠道 v2 且走插件机制。

## 1. 站内通知中心 〔定稿〕

- 数据：`notification` 表（data-model 册）；页面顶栏铃铛 + 全部已读。
- **与 Emby 事件目录的对照**（出处：3.5.2 NotificationType 枚举 18 项，
  `CoreNotificationTypes.cs:22-130`；NewLibraryContent 带 **5 秒防抖、每次最多
  10 条**（`Notifications.cs:149-170,203`）——照抄防抖与限量）：
  - `itemAdded` ← NewLibraryContent（5s 防抖、≤10 条/批，照抄）
  - `taskFailed` ← TaskFailed（`ActivityLogEntryPoint.cs:438-474`）
  - `newVersionAvailable` / `newVersionInstalled` ← ApplicationUpdateAvailable
    （不能自更新时）/ ApplicationUpdateInstalled（自更新成功）
  - `playback` / `playbackStopped` ← VideoPlayback / VideoPlaybackStopped
    （订阅项存在、**默认关**；SessionStarted/Ended 不做通知——Emby 因刷屏
    已注释掉，只进活动日志，`ActivityLogEntryPoint.cs:248-251`，照抄）
  - `pluginInstalled/pluginError`（v2 插件上线后启用）
  - Serata 自有：`danmakuReviewPending`（审核队列积压，仅管理员）、
    `announcement`（管理员群发公告，不可关）
  - 不做：CameraImageUploaded（无照片库）、UserLockedOut（登录锁定走
    登录审计页展示）
- 〔源码回填完成〕Emby 内置目录已并入上文，无增补项。

## 2. 邮件渠道 〔定稿〕

- 复用全局 SMTP 配置（唯一发信配置，密码重置安全码同源）。
- 事件→邮件映射：管理员可配哪些事件走邮件（默认：taskFailed、
  newVersionAvailable、danmakuReviewPending）。
- 邮件模板走 i18n 资源文件（web.md §7 同源）。

## 3. 用户订阅开关 〔定稿〕

每类事件用户可开关（存 `user.policy` 通知节）；管理员公告不可关。

## 4. 验收

- 事件触发 → 通知落库 + 邮件（SMTP 假服务/Testcontainers mailpit）双断言。
- 订阅关闭的事件不落库不投递（公告除外）。