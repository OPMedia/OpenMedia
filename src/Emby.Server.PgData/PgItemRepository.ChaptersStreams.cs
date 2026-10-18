using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — chapters / media_streams 读写。
    /// 对照旧 SqliteItemRepository 的 GetChapters/GetChapter/SaveChapters/
    /// GetMediaStreams/SaveMediaStreams 逐方法保留语义。
    ///
    /// 列映射：
    /// - 旧 ItemId GUID → item_id BIGINT（guid↔id 翻译）
    /// - 旧 StreamType TEXT（'Video'/'Audio'/'Subtitle'）→ stream_type SMALLINT
    ///   （schema CHECK 0=视频 1=音频 2=字幕；EmbeddedImage 流超出 CHECK 范围，保存时跳过——
    ///    嵌入封面流无播放语义，跳过比误归类为 Video 更安全，偏差已记录）
    /// - 旧 Width/Height 列存反了（旧代码注释 "backwards due to a mistake"）；
    ///   新库无历史包袱，按正确方向存取（width←Width, height←Height）
    /// - 旧 IsAvc/CodecTimeBase 列在新 schema 缺失 → 补充 DDL 增加 is_avc/codec_time_base
    /// </summary>
    public partial class PgItemRepository
    {
        /// <summary>
        /// MediaStreamType → stream_type 码（schema CHECK: 0=视频 1=音频 2=字幕）。
        /// </summary>
        private static int? MapStreamType(MediaStreamType type)
        {
            switch (type)
            {
                case MediaStreamType.Video:
                    return 0;
                case MediaStreamType.Audio:
                    return 1;
                case MediaStreamType.Subtitle:
                    return 2;
                default:
                    // EmbeddedImage 等超出 CHECK 范围
                    return null;
            }
        }

        private static MediaStreamType MapStreamType(int code)
        {
            switch (code)
            {
                case 0: return MediaStreamType.Video;
                case 1: return MediaStreamType.Audio;
                default: return MediaStreamType.Subtitle;
            }
        }

        // ===========================================================
        // Chapters
        // ===========================================================

        /// <summary>
        /// 获取章节（旧 GetChapters：按 ChapterIndex 升序）。
        /// </summary>
        public async Task<List<ChapterInfo>> GetChaptersAsync(BaseItem item, CancellationToken cancellationToken = default)
        {
            CheckDisposed();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<ChapterInfo>();

            await using (var cmd = new NpgsqlCommand(
                @"SELECT start_position_ticks, name, image_path, image_date_modified
                  FROM chapters
                  WHERE item_id = (SELECT id FROM media_items WHERE guid = @guid LIMIT 1)
                  ORDER BY chapter_index ASC",
                connection))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(GetChapter(reader, item));
                }
            }

            return list;
        }

        /// <summary>
        /// 获取单个章节（旧 GetChapter）。
        /// </summary>
        public async Task<ChapterInfo> GetChapterAsync(BaseItem item, int index, CancellationToken cancellationToken = default)
        {
            CheckDisposed();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                @"SELECT start_position_ticks, name, image_path, image_date_modified
                  FROM chapters
                  WHERE item_id = (SELECT id FROM media_items WHERE guid = @guid LIMIT 1)
                    AND chapter_index = @chapter_index",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });
            cmd.Parameters.Add(new NpgsqlParameter("chapter_index", NpgsqlDbType.Integer) { Value = index });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return GetChapter(reader, item);
            }

            return null;
        }

        private ChapterInfo GetChapter(NpgsqlDataReader reader, BaseItem item)
        {
            var chapter = new ChapterInfo
            {
                StartPositionTicks = reader.GetInt64(0)
            };

            if (!reader.IsDBNull(1))
            {
                chapter.Name = reader.GetString(1);
            }

            if (!reader.IsDBNull(2))
            {
                chapter.ImagePath = reader.GetString(2);

                if (!string.IsNullOrEmpty(chapter.ImagePath))
                {
                    chapter.ImageTag = ImageProcessor != null
                        ? ImageProcessor.GetImageCacheTag(item, chapter)
                        : null;
                }
            }

            if (!reader.IsDBNull(3))
            {
                chapter.ImageDateModified = reader.GetDateTime(3);
            }

            return chapter;
        }

        /// <summary>
        /// 保存章节（旧 SaveChapters：先删后插，ChapterIndex 按列表顺序重编号）。
        /// </summary>
        public Task SaveChaptersAsync(Guid id, List<ChapterInfo> chapters, CancellationToken cancellationToken = default)
        {
            CheckDisposed();

            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("id");
            }

            if (chapters == null)
            {
                throw new ArgumentNullException("chapters");
            }

            var validChapters = chapters.Where(c => c != null).ToList();

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var internalId = await GetInternalIdAsync(connection, transaction, id, ct).ConfigureAwait(false);
                if (internalId == null)
                {
                    return;
                }

                var itemId = internalId.Value;

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "DELETE FROM chapters WHERE item_id = @item_id",
                    ct,
                    new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);

                if (validChapters.Count == 0)
                {
                    return;
                }

                // 单条 UNNEST 批量插入全部章节
                var count = validChapters.Count;
                var indices = Enumerable.Range(0, count).ToArray();
                var ticks = validChapters.Select(c => c.StartPositionTicks).ToArray();
                var names = validChapters.Select(c => c.Name).ToArray();
                var imagePaths = validChapters.Select(c => c.ImagePath).ToArray();
                var imageDates = validChapters
                    .Select(c => c.ImageDateModified != default(DateTime) ? (object)ToUtcDateTime(c.ImageDateModified) : DBNull.Value)
                    .ToArray();

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO chapters
                        (item_id, chapter_index, start_position_ticks, name, image_path, image_date_modified)
                      SELECT @item_id, u.idx, u.ticks, u.name, u.image_path, u.image_date_modified
                      FROM UNNEST(@indices, @ticks, @names, @image_paths, @image_dates)
                           AS u(idx, ticks, name, image_path, image_date_modified)",
                    ct,
                    new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId },
                    new NpgsqlParameter("indices", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = indices },
                    new NpgsqlParameter("ticks", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ticks },
                    new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = names },
                    new NpgsqlParameter("image_paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = imagePaths },
                    new NpgsqlParameter("image_dates", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = imageDates }).ConfigureAwait(false);
            }, cancellationToken);
        }

        // ===========================================================
        // MediaStreams
        // ===========================================================

        /// <summary>
        /// 获取媒体流（旧 GetMediaStreams：ItemId + 可选 Type/Index 过滤，按 StreamIndex 升序）。
        /// </summary>
        public async Task<List<MediaStream>> GetMediaStreamsAsync(MediaStreamQuery query, CancellationToken cancellationToken = default)
        {
            CheckDisposed();

            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            var cmdText = @"SELECT item_id, stream_index, stream_type, codec, language, channel_layout,
                                   profile, aspect_ratio, path, is_interlaced, bit_rate, channels,
                                   sample_rate, is_default, is_forced, is_external, height, width,
                                   average_frame_rate, real_frame_rate, level, pixel_format, bit_depth,
                                   is_anamorphic, ref_frames, codec_tag, comment, nal_length_size,
                                   is_avc, title, time_base, codec_time_base, color_primaries,
                                   color_space, color_transfer
                            FROM media_streams
                            WHERE item_id = (SELECT id FROM media_items WHERE guid = @guid LIMIT 1)";

            if (query.Type.HasValue)
            {
                var code = MapStreamType(query.Type.Value);
                if (code == null)
                {
                    return new List<MediaStream>();
                }

                cmdText += " AND stream_type = @stream_type";
            }

            if (query.Index.HasValue)
            {
                cmdText += " AND stream_index = @stream_index";
            }

            cmdText += " ORDER BY stream_index ASC";

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<MediaStream>();

            await using (var cmd = new NpgsqlCommand(cmdText, connection))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = query.ItemId });

                if (query.Type.HasValue)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("stream_type", NpgsqlDbType.Smallint) { Value = MapStreamType(query.Type.Value) });
                }

                if (query.Index.HasValue)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("stream_index", NpgsqlDbType.Integer) { Value = query.Index.Value });
                }

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(GetMediaStream(reader));
                }
            }

            return list;
        }

        /// <summary>
        /// 保存媒体流（旧 SaveMediaStreams：先删后插）。
        /// </summary>
        public Task SaveMediaStreamsAsync(Guid id, List<MediaStream> streams, CancellationToken cancellationToken)
        {
            CheckDisposed();

            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (streams == null)
            {
                throw new ArgumentNullException(nameof(streams));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 事务外完成映射与过滤（EmbeddedImage 等超出 schema CHECK 的类型跳过）
            var rows = streams
                .Where(s => s != null)
                .Select(s => new { Stream = s, Type = MapStreamType(s.Type) })
                .Where(x => x.Type != null)
                .OrderBy(x => x.Stream.Index)
                .ToList();

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var internalId = await GetInternalIdAsync(connection, transaction, id, ct).ConfigureAwait(false);
                if (internalId == null)
                {
                    return;
                }

                var itemId = internalId.Value;

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "DELETE FROM media_streams WHERE item_id = @item_id",
                    ct,
                    new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);

                if (rows.Count == 0)
                {
                    return;
                }

                // 单条 UNNEST 批量插入全部媒体流（1 次网络往返）
                var n = rows.Count;
                var indices = new int[n];
                var types = new short[n];
                var codecs = new string[n];
                var languages = new string[n];
                var channelLayouts = new string[n];
                var profiles = new string[n];
                var aspectRatios = new string[n];
                var paths = new string[n];
                var interlaced = new bool[n];
                var bitRates = new object[n];
                var channels = new object[n];
                var sampleRates = new object[n];
                var isDefaults = new bool[n];
                var isForceds = new bool[n];
                var isExternals = new bool[n];
                var heights = new object[n];
                var widths = new object[n];
                var avgFrameRates = new object[n];
                var realFrameRates = new object[n];
                var levels = new object[n];
                var pixelFormats = new string[n];
                var bitDepths = new object[n];
                var isAnamorphics = new object[n];
                var refFrames = new object[n];
                var codecTags = new string[n];
                var comments = new string[n];
                var nalLengthSizes = new string[n];
                var isAvcs = new object[n];
                var titles = new string[n];
                var timeBases = new string[n];
                var codecTimeBases = new string[n];
                var colorPrimaries = new string[n];
                var colorSpaces = new string[n];
                var colorTransfers = new string[n];

                for (int i = 0; i < n; i++)
                {
                    var x = rows[i];
                    var st = x.Stream;

                    indices[i] = st.Index;
                    types[i] = (short)x.Type.Value;
                    codecs[i] = st.Codec;
                    languages[i] = st.Language;
                    channelLayouts[i] = st.ChannelLayout;
                    profiles[i] = st.Profile;
                    aspectRatios[i] = st.AspectRatio;
                    paths[i] = GetPathToSave(st.Path);
                    interlaced[i] = st.IsInterlaced;
                    bitRates[i] = ToDbValue(st.BitRate);
                    channels[i] = ToDbValue(st.Channels);
                    sampleRates[i] = ToDbValue(st.SampleRate);
                    isDefaults[i] = st.IsDefault;
                    isForceds[i] = st.IsForced;
                    isExternals[i] = st.IsExternal;
                    heights[i] = st.Height.HasValue && st.Height.Value > 0 ? (object)st.Height.Value : DBNull.Value;
                    widths[i] = st.Width.HasValue && st.Width.Value > 0 ? (object)st.Width.Value : DBNull.Value;
                    avgFrameRates[i] = st.AverageFrameRate.HasValue ? (object)(double)st.AverageFrameRate.Value : DBNull.Value;
                    realFrameRates[i] = st.RealFrameRate.HasValue ? (object)(double)st.RealFrameRate.Value : DBNull.Value;
                    levels[i] = ToDbValue(st.Level);
                    pixelFormats[i] = st.PixelFormat;
                    bitDepths[i] = ToDbValue(st.BitDepth);
                    isAnamorphics[i] = st.IsAnamorphic.HasValue ? (object)st.IsAnamorphic.Value : DBNull.Value;
                    refFrames[i] = ToDbValue(st.RefFrames);
                    codecTags[i] = st.CodecTag;
                    comments[i] = st.Comment;
                    nalLengthSizes[i] = st.NalLengthSize;
                    isAvcs[i] = st.IsAVC.HasValue ? (object)st.IsAVC.Value : DBNull.Value;
                    titles[i] = st.Title;
                    timeBases[i] = st.TimeBase;
                    codecTimeBases[i] = st.CodecTimeBase;
                    colorPrimaries[i] = st.ColorPrimaries;
                    colorSpaces[i] = st.ColorSpace;
                    colorTransfers[i] = st.ColorTransfer;
                }

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO media_streams
                        (item_id, stream_index, stream_type, codec, language, channel_layout,
                         profile, aspect_ratio, path, is_interlaced, bit_rate, channels,
                         sample_rate, is_default, is_forced, is_external, height, width,
                         average_frame_rate, real_frame_rate, level, pixel_format, bit_depth,
                         is_anamorphic, ref_frames, codec_tag, comment, nal_length_size,
                         is_avc, title, time_base, codec_time_base, color_primaries,
                         color_space, color_transfer)
                      SELECT
                        @item_id, u.stream_index, u.stream_type, u.codec, u.language, u.channel_layout,
                        u.profile, u.aspect_ratio, u.path, u.is_interlaced, u.bit_rate, u.channels,
                        u.sample_rate, u.is_default, u.is_forced, u.is_external, u.height, u.width,
                        u.average_frame_rate, u.real_frame_rate, u.level, u.pixel_format, u.bit_depth,
                        u.is_anamorphic, u.ref_frames, u.codec_tag, u.comment, u.nal_length_size,
                        u.is_avc, u.title, u.time_base, u.codec_time_base, u.color_primaries,
                        u.color_space, u.color_transfer
                      FROM UNNEST(
                        @indices, @types, @codecs, @languages, @channel_layouts,
                        @profiles, @aspect_ratios, @paths, @interlaceds, @bit_rates, @channels,
                        @sample_rates, @is_defaults, @is_forceds, @is_externals, @heights, @widths,
                        @avg_frame_rates, @real_frame_rates, @levels, @pixel_formats, @bit_depths,
                        @is_anamorphics, @ref_frames, @codec_tags, @comments, @nal_length_sizes,
                        @is_avcs, @titles, @time_bases, @codec_time_bases, @color_primaries,
                        @color_spaces, @color_transfers
                      ) AS u(
                        stream_index, stream_type, codec, language, channel_layout,
                        profile, aspect_ratio, path, is_interlaced, bit_rate, channels,
                        sample_rate, is_default, is_forced, is_external, height, width,
                        average_frame_rate, real_frame_rate, level, pixel_format, bit_depth,
                        is_anamorphic, ref_frames, codec_tag, comment, nal_length_size,
                        is_avc, title, time_base, codec_time_base, color_primaries,
                        color_space, color_transfer
                      )",
                    ct,
                    new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId },
                    new NpgsqlParameter("indices", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = indices },
                    new NpgsqlParameter("types", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = types },
                    new NpgsqlParameter("codecs", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = codecs },
                    new NpgsqlParameter("languages", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = languages },
                    new NpgsqlParameter("channel_layouts", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = channelLayouts },
                    new NpgsqlParameter("profiles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = profiles },
                    new NpgsqlParameter("aspect_ratios", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = aspectRatios },
                    new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = paths },
                    new NpgsqlParameter("interlaceds", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = interlaced },
                    new NpgsqlParameter("bit_rates", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = bitRates },
                    new NpgsqlParameter("channels", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = channels },
                    new NpgsqlParameter("sample_rates", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = sampleRates },
                    new NpgsqlParameter("is_defaults", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isDefaults },
                    new NpgsqlParameter("is_forceds", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isForceds },
                    new NpgsqlParameter("is_externals", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isExternals },
                    new NpgsqlParameter("heights", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = heights },
                    new NpgsqlParameter("widths", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = widths },
                    new NpgsqlParameter("avg_frame_rates", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = avgFrameRates },
                    new NpgsqlParameter("real_frame_rates", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = realFrameRates },
                    new NpgsqlParameter("levels", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = levels },
                    new NpgsqlParameter("pixel_formats", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = pixelFormats },
                    new NpgsqlParameter("bit_depths", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = bitDepths },
                    new NpgsqlParameter("is_anamorphics", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isAnamorphics },
                    new NpgsqlParameter("ref_frames", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = refFrames },
                    new NpgsqlParameter("codec_tags", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = codecTags },
                    new NpgsqlParameter("comments", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = comments },
                    new NpgsqlParameter("nal_length_sizes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = nalLengthSizes },
                    new NpgsqlParameter("is_avcs", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isAvcs },
                    new NpgsqlParameter("titles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = titles },
                    new NpgsqlParameter("time_bases", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = timeBases },
                    new NpgsqlParameter("codec_time_bases", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = codecTimeBases },
                    new NpgsqlParameter("color_primaries", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = colorPrimaries },
                    new NpgsqlParameter("color_spaces", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = colorSpaces },
                    new NpgsqlParameter("color_transfers", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = colorTransfers }).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// 行 → MediaStream（对照旧 GetMediaStream 逐字段）。
        /// </summary>
        private MediaStream GetMediaStream(NpgsqlDataReader reader)
        {
            var item = new MediaStream
            {
                Index = reader.GetInt32(1)
            };

            item.Type = MapStreamType(reader.GetInt16(2));

            if (!reader.IsDBNull(3))
            {
                item.Codec = reader.GetString(3);
            }

            if (!reader.IsDBNull(4))
            {
                item.Language = reader.GetString(4);
            }

            if (!reader.IsDBNull(5))
            {
                item.ChannelLayout = reader.GetString(5);
            }

            if (!reader.IsDBNull(6))
            {
                item.Profile = reader.GetString(6);
            }

            if (!reader.IsDBNull(7))
            {
                item.AspectRatio = reader.GetString(7);
            }

            if (!reader.IsDBNull(8))
            {
                item.Path = RestorePath(reader.GetString(8));
            }

            item.IsInterlaced = reader.GetBoolean(9);

            if (!reader.IsDBNull(10))
            {
                item.BitRate = reader.GetInt32(10);
            }

            if (!reader.IsDBNull(11))
            {
                item.Channels = reader.GetInt16(11);
            }

            if (!reader.IsDBNull(12))
            {
                item.SampleRate = reader.GetInt32(12);
            }

            item.IsDefault = reader.GetBoolean(13);
            item.IsForced = reader.GetBoolean(14);
            item.IsExternal = reader.GetBoolean(15);

            if (!reader.IsDBNull(16))
            {
                item.Height = reader.GetInt32(16);
            }

            if (!reader.IsDBNull(17))
            {
                item.Width = reader.GetInt32(17);
            }

            if (!reader.IsDBNull(18))
            {
                item.AverageFrameRate = (float)reader.GetDouble(18);
            }

            if (!reader.IsDBNull(19))
            {
                item.RealFrameRate = (float)reader.GetDouble(19);
            }

            if (!reader.IsDBNull(20))
            {
                item.Level = reader.GetDouble(20);
            }

            if (!reader.IsDBNull(21))
            {
                item.PixelFormat = reader.GetString(21);
            }

            if (!reader.IsDBNull(22))
            {
                item.BitDepth = reader.GetInt16(22);
            }

            if (!reader.IsDBNull(23))
            {
                item.IsAnamorphic = reader.GetBoolean(23);
            }

            if (!reader.IsDBNull(24))
            {
                item.RefFrames = reader.GetInt16(24);
            }

            if (!reader.IsDBNull(25))
            {
                item.CodecTag = reader.GetString(25);
            }

            if (!reader.IsDBNull(26))
            {
                item.Comment = reader.GetString(26);
            }

            if (!reader.IsDBNull(27))
            {
                item.NalLengthSize = reader.GetString(27);
            }

            if (!reader.IsDBNull(28))
            {
                item.IsAVC = reader.GetBoolean(28);
            }

            if (!reader.IsDBNull(29))
            {
                item.Title = reader.GetString(29);
            }

            if (!reader.IsDBNull(30))
            {
                item.TimeBase = reader.GetString(30);
            }

            if (!reader.IsDBNull(31))
            {
                item.CodecTimeBase = reader.GetString(31);
            }

            if (!reader.IsDBNull(32))
            {
                item.ColorPrimaries = reader.GetString(32);
            }

            if (!reader.IsDBNull(33))
            {
                item.ColorSpace = reader.GetString(33);
            }

            if (!reader.IsDBNull(34))
            {
                item.ColorTransfer = reader.GetString(34);
            }

            return item;
        }
    }
}
