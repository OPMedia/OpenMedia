using System;
using System.IO;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace Emby.Server.Implementations.IO
{
    public class MbLinkShortcutHandler : IShortcutHandler
    {
        private readonly IFileSystem _fileSystem;

        public MbLinkShortcutHandler(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public string Extension
        {
            get { return ".mblink"; }
        }

        public string Resolve(string shortcutPath)
        {
            if (string.IsNullOrEmpty(shortcutPath))
            {
                throw new ArgumentNullException("filenshortcutPathame");
            }

            if (string.Equals(Path.GetExtension(shortcutPath), ".mblink", StringComparison.OrdinalIgnoreCase))
            {
                var path = _fileSystem.ReadAllText(shortcutPath);

                // 斜杠方向归一化（仅 Windows 分隔符）：REST 客户端建库常传正斜杠，
                // 而库内路径与 DB path 均为反斜杠，下游精确比较（CollectionFolder.
                // GetPhysicalParents 的内存树匹配 / FindByPath 的 DB 匹配）失配
                // 会导致 PhysicalFolderIds 为空、用户作用域递归查询丢整个库。
                // %占位符% 路径不含 '/' 不受影响；UNC //server → \\server 等价。
                if (path.IndexOf('/') >= 0)
                {
                    path = path.Replace('/', '\\');
                }

                return _fileSystem.NormalizePath(path);
            }

            return null;
        }

        public void Create(string shortcutPath, string targetPath)
        {
            if (string.IsNullOrEmpty(shortcutPath))
            {
                throw new ArgumentNullException("shortcutPath");
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                throw new ArgumentNullException("targetPath");
            }

            _fileSystem.WriteAllText(shortcutPath, targetPath);
        }
    }
}
