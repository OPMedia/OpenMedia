/**
 * Emby 4.8 兼容 DTO 形态(SPEC/api:DTO 与 Id 形态照搬,ADR-0004)。
 * 仅展开 UI 消费的字段;缺的字段后续按 openapi.json 补。
 * Id 一律为 Emby 数字字符串形态(SPEC/core §4)。
 */

/** Emby Item 类型(data-model.md:item.type) */
export type ItemType =
  | 'Movie'
  | 'Series'
  | 'Season'
  | 'Episode'
  | 'BoxSet'
  | 'Playlist'
  | 'Folder'
  | 'CollectionFolder'
  | 'Person'
  | 'Trailer'
  | 'MusicAlbum'
  | 'Audio'

/** 用户数据(SPEC/data-model:user_data 表的 API 投影) */
export interface UserItemData {
  Rating?: number | null
  PlayedPercentage?: number
  UnplayedItemCount?: number
  PlaybackPositionTicks?: number
  PlayCount?: number
  IsFavorite?: boolean
  Likes?: boolean | null
  LastPlayedDate?: string
  Played?: boolean
  Key?: string
}

/** ImageTags:type → tag(GET /Items/{id}/Images/{type} 用 tag 作缓存键) */
export type ImageTagMap = Record<string, string>

export interface BaseItemPerson {
  Name: string
  Id: string
  Role?: string
  Type?: string // Actor | Director | Writer
  PrimaryImageTag?: string
}

/** 媒体流(SPEC/data-model:media_stream) */
export interface MediaStream {
  Type: 'Video' | 'Audio' | 'Subtitle'
  Codec: string
  Index: number
  Language?: string
  DisplayTitle?: string
  Title?: string
  IsDefault?: boolean
  IsForced?: boolean
  /** SPEC/api §1.3:外挂字幕必须带 DeliveryUrl(原格式直传) */
  DeliveryMethod?: 'External' | 'Embed' | 'Hls'
  DeliveryUrl?: string
  // 探测列
  Width?: number
  Height?: number
  BitRate?: number
  Channels?: number
  SampleRate?: number
}

/** 媒体来源(SPEC/data-model:media_source;api.md §1.2 形状) */
export interface MediaSourceInfo {
  Id: string
  Path?: string
  Protocol?: 'Local' | 'Http' | 'Strm'
  Container?: string
  Size?: number | null
  IsRemote?: boolean
  Name?: string
  Bitrate?: number
  MediaStreams: MediaStream[]
}

/** 条目 DTO(字段名与 Emby BaseItemDto 对齐) */
export interface BaseItemDto {
  Name: string
  OriginalTitle?: string
  Id: string
  ServerId?: string
  Type: ItemType
  /** 库/父条目 */
  CollectionType?: 'movies' | 'tvshows'
  ParentId?: string
  SeriesId?: string
  SeriesName?: string
  SeasonId?: string
  SeasonName?: string
  SeasonNumber?: number
  EpisodeNumber?: number
  SortName?: string
  Overview?: string
  Genres?: string[]
  ProductionYear?: number
  EndYear?: number
  PremiereDate?: string
  DateCreated?: string
  CommunityRating?: number
  OfficialRating?: string
  RunTimeTicks?: number
  /** 100ns ticks(SPEC/core §4 时间规范) */
  Taglines?: string[]
  People?: BaseItemPerson[]
  Studios?: { Name: string; Id: string }[]
  /** 标签(真实站"标签"tab 数据源) */
  Tags?: string[]
  /** 文件夹型条目(真实 BaseItemDto.IsFolder) */
  IsFolder?: boolean
  /** Virtual / FileSystem / Remote 等(真实 LocationType) */
  LocationType?: string
  ImageTags?: ImageTagMap
  /** 首图宽高比(PrimaryImageAspectRatio Fields) */
  PrimaryImageAspectRatio?: number
  BackdropImageTags?: string[]
  MediaSources?: MediaSourceInfo[]
  /** 版本数(真实站 MediaSourceCount) */
  MediaSourceCount?: number
  UserData?: UserItemData
  /** locked_fields(SPEC/data-model;text[],迁移锁定字段) */
  LockedFields?: string[]
  ChildCount?: number
  /** 库条目(虚拟文件夹) */
  Locations?: string[]
}

/** Emby 查询响应(QueryResult) */
export interface QueryResult<T = BaseItemDto> {
  Items: T[]
  TotalRecordCount: number
  StartIndex: number
}

export interface UserPolicy {
  IsAdministrator?: boolean
  EnableAllFolders?: boolean
  EnabledFolders?: string[]
  IsHidden?: boolean
  [key: string]: unknown
}

export interface UserDto {
  Id: string
  Name: string
  ServerId?: string
  IsAdministrator?: boolean
  HasPassword?: boolean
  PrimaryImageTag?: string
  Policy?: UserPolicy
  DateCreated?: string
}

/** POST /Users/AuthenticateByName 响应 */
export interface AuthenticationResult {
  User: UserDto
  AccessToken: string
  ServerId?: string
}

/** POST /Items/{id}/PlaybackInfo 响应(api.md §1.2) */
export interface PlaybackInfoResponse {
  MediaSources: MediaSourceInfo[]
  PlaySessionId: string
  /** 唯一权威交付地址(URL 可重签 api_key) */
  DirectStreamUrl?: string
}

/** GET /System/Info */
export interface SystemInfo {
  ServerName: string
  Version: string
  Id: string
  OperatingSystem: string
  ApiVersion?: string
}

/** GET /Items/Counts */
export interface ItemCounts {
  MovieCount: number
  SeriesCount: number
  EpisodeCount: number
  BoxSetCount: number
}
