using System;
using System.Collections.Generic;
using System.Threading;
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
        public List<ChapterInfo> GetChapters(BaseItem item)
        {
            CheckDisposed();

            using (var connection = CreateConnection())
            {
                var list = new List<ChapterInfo>();

                using (var cmd = new NpgsqlCommand(
                    @"SELECT start_position_ticks, name, image_path, image_date_modified
                      FROM chapters
                      WHERE item_id = @item_id
                      ORDER BY chapter_index ASC",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint)
                    {
                        Value = GetInternalId(connection, item.Id) ?? -1L
                    });

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(GetChapter(reader, item));
                        }
                    }
                }

                return list;
            }
        }

        /// <summary>
        /// 获取单个章节（旧 GetChapter）。
        /// </summary>
        public ChapterInfo GetChapter(BaseItem item, int index)
        {
            CheckDisposed();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    @"SELECT start_position_ticks, name, image_path, image_date_modified
                      FROM chapters
                      WHERE item_id = @item_id AND chapter_index = @chapter_index",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint)
                    {
                        Value = GetInternalId(connection, item.Id) ?? -1L
                    });
                    cmd.Parameters.Add(new NpgsqlParameter("chapter_index", NpgsqlDbType.Integer) { Value = index });

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return GetChapter(reader, item);
                        }
                    }
                }
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
        public void SaveChapters(Guid id, List<ChapterInfo> chapters)
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

            RunInTransaction((connection, transaction) =>
            {
                var internalId = GetInternalId(connection, transaction, id);

                if (internalId == null)
                {
                    return;
                }

                var itemId = internalId.Value;

                using (var deleteCmd = new NpgsqlCommand(
                    "DELETE FROM chapters WHERE item_id = @item_id",
                    connection, transaction))
                {
                    deleteCmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId });
                    deleteCmd.ExecuteNonQuery();
                }

                var chapterIndex = 0;

                foreach (var chapter in chapters)
                {
                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO chapters
                            (item_id, chapter_index, start_position_ticks, name, image_path, image_date_modified)
                          VALUES
                            (@item_id, @chapter_index, @start_position_ticks, @name, @image_path, @image_date_modified)",
                        connection, transaction))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId });
                        cmd.Parameters.Add(new NpgsqlParameter("chapter_index", NpgsqlDbType.Integer) { Value = chapterIndex });
                        cmd.Parameters.Add(new NpgsqlParameter("start_position_ticks", NpgsqlDbType.Bigint) { Value = chapter.StartPositionTicks });
                        cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = (object)chapter.Name ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("image_path", NpgsqlDbType.Text) { Value = (object)chapter.ImagePath ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("image_date_modified", NpgsqlDbType.TimestampTz)
                        {
                            Value = chapter.ImageDateModified != default(DateTime)
                                ? (object)ToUtcDateTime(chapter.ImageDateModified)
                                : DBNull.Value
                        });

                        cmd.ExecuteNonQuery();
                    }

                    chapterIndex++;
                }
            });
        }

        // ===========================================================
        // MediaStreams
        // ===========================================================

        /// <summary>
        /// 获取媒体流（旧 GetMediaStreams：ItemId + 可选 Type/Index 过滤，按 StreamIndex 升序）。
        /// </summary>
        public List<MediaStream> GetMediaStreams(MediaStreamQuery query)
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
                            WHERE item_id = @item_id";

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

            using (var connection = CreateConnection())
            {
                var list = new List<MediaStream>();

                using (var cmd = new NpgsqlCommand(cmdText, connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint)
                    {
                        Value = GetInternalId(connection, query.ItemId) ?? -1L
                    });

                    if (query.Type.HasValue)
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("stream_type", NpgsqlDbType.Smallint) { Value = MapStreamType(query.Type.Value) });
                    }

                    if (query.Index.HasValue)
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("stream_index", NpgsqlDbType.Integer) { Value = query.Index.Value });
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(GetMediaStream(reader));
                        }
                    }
                }

                return list;
            }
        }

        /// <summary>
        /// 保存媒体流（旧 SaveMediaStreams：先删后插）。
        /// </summary>
        public void SaveMediaStreams(Guid id, List<MediaStream> streams, CancellationToken cancellationToken)
        {
            CheckDisposed();

            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("id");
            }

            if (streams == null)
            {
                throw new ArgumentNullException("streams");
            }

            cancellationToken.ThrowIfCancellationRequested();

            RunInTransaction((connection, transaction) =>
            {
                var internalId = GetInternalId(connection, transaction, id);

                if (internalId == null)
                {
                    return;
                }

                var itemId = internalId.Value;

                using (var deleteCmd = new NpgsqlCommand(
                    "DELETE FROM media_streams WHERE item_id = @item_id",
                    connection, transaction))
                {
                    deleteCmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId });
                    deleteCmd.ExecuteNonQuery();
                }

                foreach (var stream in streams)
                {
                    var streamType = MapStreamType(stream.Type);

                    if (streamType == null)
                    {
                        // EmbeddedImage 等超出 schema CHECK (0,1,2)，跳过（见类注释）
                        continue;
                    }

                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO media_streams
                            (item_id, stream_index, stream_type, codec, language, channel_layout,
                             profile, aspect_ratio, path, is_interlaced, bit_rate, channels,
                             sample_rate, is_default, is_forced, is_external, height, width,
                             average_frame_rate, real_frame_rate, level, pixel_format, bit_depth,
                             is_anamorphic, ref_frames, codec_tag, comment, nal_length_size,
                             is_avc, title, time_base, codec_time_base, color_primaries,
                             color_space, color_transfer)
                          VALUES
                            (@item_id, @stream_index, @stream_type, @codec, @language, @channel_layout,
                             @profile, @aspect_ratio, @path, @is_interlaced, @bit_rate, @channels,
                             @sample_rate, @is_default, @is_forced, @is_external, @height, @width,
                             @average_frame_rate, @real_frame_rate, @level, @pixel_format, @bit_depth,
                             @is_anamorphic, @ref_frames, @codec_tag, @comment, @nal_length_size,
                             @is_avc, @title, @time_base, @codec_time_base, @color_primaries,
                             @color_space, @color_transfer)",
                        connection, transaction))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemId });
                        cmd.Parameters.Add(new NpgsqlParameter("stream_index", NpgsqlDbType.Integer) { Value = stream.Index });
                        cmd.Parameters.Add(new NpgsqlParameter("stream_type", NpgsqlDbType.Smallint) { Value = streamType });
                        cmd.Parameters.Add(new NpgsqlParameter("codec", NpgsqlDbType.Text) { Value = (object)stream.Codec ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("language", NpgsqlDbType.Text) { Value = (object)stream.Language ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("channel_layout", NpgsqlDbType.Text) { Value = (object)stream.ChannelLayout ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("profile", NpgsqlDbType.Text) { Value = (object)stream.Profile ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("aspect_ratio", NpgsqlDbType.Text) { Value = (object)stream.AspectRatio ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Text) { Value = (object)GetPathToSave(stream.Path) ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("is_interlaced", NpgsqlDbType.Boolean) { Value = stream.IsInterlaced });
                        cmd.Parameters.Add(new NpgsqlParameter("bit_rate", NpgsqlDbType.Integer)
                        {
                            Value = stream.BitRate.HasValue ? (object)stream.BitRate.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("channels", NpgsqlDbType.Smallint)
                        {
                            Value = stream.Channels.HasValue ? (object)stream.Channels.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("sample_rate", NpgsqlDbType.Integer)
                        {
                            Value = stream.SampleRate.HasValue ? (object)stream.SampleRate.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("is_default", NpgsqlDbType.Boolean) { Value = stream.IsDefault });
                        cmd.Parameters.Add(new NpgsqlParameter("is_forced", NpgsqlDbType.Boolean) { Value = stream.IsForced });
                        cmd.Parameters.Add(new NpgsqlParameter("is_external", NpgsqlDbType.Boolean) { Value = stream.IsExternal });
                        cmd.Parameters.Add(new NpgsqlParameter("height", NpgsqlDbType.Integer)
                        {
                            Value = stream.Height.HasValue && stream.Height.Value > 0 ? (object)stream.Height.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("width", NpgsqlDbType.Integer)
                        {
                            Value = stream.Width.HasValue && stream.Width.Value > 0 ? (object)stream.Width.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("average_frame_rate", NpgsqlDbType.Double)
                        {
                            Value = stream.AverageFrameRate.HasValue ? (object)(double)stream.AverageFrameRate.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("real_frame_rate", NpgsqlDbType.Double)
                        {
                            Value = stream.RealFrameRate.HasValue ? (object)(double)stream.RealFrameRate.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("level", NpgsqlDbType.Double)
                        {
                            Value = stream.Level.HasValue ? (object)stream.Level.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("pixel_format", NpgsqlDbType.Text) { Value = (object)stream.PixelFormat ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("bit_depth", NpgsqlDbType.Smallint)
                        {
                            Value = stream.BitDepth.HasValue ? (object)stream.BitDepth.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("is_anamorphic", NpgsqlDbType.Boolean)
                        {
                            Value = stream.IsAnamorphic.HasValue ? (object)stream.IsAnamorphic.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("ref_frames", NpgsqlDbType.Smallint)
                        {
                            Value = stream.RefFrames.HasValue ? (object)stream.RefFrames.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("codec_tag", NpgsqlDbType.Text) { Value = (object)stream.CodecTag ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("comment", NpgsqlDbType.Text) { Value = (object)stream.Comment ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("nal_length_size", NpgsqlDbType.Text) { Value = (object)stream.NalLengthSize ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("is_avc", NpgsqlDbType.Boolean)
                        {
                            Value = stream.IsAVC.HasValue ? (object)stream.IsAVC.Value : DBNull.Value
                        });
                        cmd.Parameters.Add(new NpgsqlParameter("title", NpgsqlDbType.Text) { Value = (object)stream.Title ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("time_base", NpgsqlDbType.Text) { Value = (object)stream.TimeBase ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("codec_time_base", NpgsqlDbType.Text) { Value = (object)stream.CodecTimeBase ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("color_primaries", NpgsqlDbType.Text) { Value = (object)stream.ColorPrimaries ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("color_space", NpgsqlDbType.Text) { Value = (object)stream.ColorSpace ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("color_transfer", NpgsqlDbType.Text) { Value = (object)stream.ColorTransfer ?? DBNull.Value });

                        cmd.ExecuteNonQuery();
                    }
                }
            });
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
