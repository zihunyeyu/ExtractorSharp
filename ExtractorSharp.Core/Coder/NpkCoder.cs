﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExtractorSharp.Core.Handle;
using ExtractorSharp.Core.Lib;
using ExtractorSharp.Core.Model;
using ExtractorSharp.Exceptions;

namespace ExtractorSharp.Core.Coder {
    public static class NpkCoder {
        public const string NPK_FlAG = "NeoplePack_Bill";
        public const string IMG_FLAG = "Neople Img File";
        public const string IMAGE_FLAG = "Neople Image File";

        public const string IMAGE_DIR = "ImagePacks2";

        public const string SOUND_DIR = "SoundPacks";

        private static char[] key;

        private static char[] Key {
            get {
                if (key != null) {
                    return key;
                }
                var cs = new char[256];
                var temp = "puchikon@neople dungeon and fighter ".ToArray();
                temp.CopyTo(cs, 0);
                var ds = new[] { 'D', 'N', 'F' };
                for (var i = temp.Length; i < 255; i++) cs[i] = ds[i % 3];
                cs[255] = '\0';
                return key = cs;
            }
        }

        /// <summary>
        ///     读取img路径
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private static string ReadPath(this Stream stream) {
            var data = new byte[256];
            var i = 0;
            while (i < 256) {
                data[i] = (byte)(stream.ReadByte() ^ Key[i]);
                if (data[i] == 0) {
                    break;
                }
                i++;
            }
            stream.Seek(255 - i); //防止因加密导致的文件名读取错误
            return Encoding.Default.GetString(data).Replace("\0", "");
        }


        /// <summary>
        ///     写入img路径
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="str"></param>
        private static void WritePath(this Stream stream, string str) {
            var data = new byte[256];
            var temp = Encoding.Default.GetBytes(str);
            temp.CopyTo(data, 0);
            for (var i = 0; i < data.Length; i++) {
                data[i] = (byte)(data[i] ^ Key[i]);
            }
            stream.Write(data);
        }

        /// <summary>
        ///     读取一个贴图
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static Bitmap ReadImage(Stream stream, Sprite entity) {
            var data = new byte[entity.Width * entity.Height * 4];
            for (var i = 0; i < data.Length; i += 4) {
                var bits = entity.Type;
                if (entity.Version == ImgVersion.Ver4 && bits == ColorBits.ARGB_1555) {
                    bits = ColorBits.ARGB_8888;
                }
                var temp = Colors.ReadColor(stream, bits);
                temp.CopyTo(data, i);
            }
            return Bitmaps.FromArray(data, entity.Size);
        }


        /// <summary>
        ///     计算NPK的校验码
        /// </summary>
        /// <param name="count"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private static byte[] CompileHash(byte[] source) {
            if (source.Length < 1) {
                return new byte[0];
            }
            var count = source.Length / 17 * 17;
            var data = new byte[count];
            Array.Copy(source, 0, data, 0, count);
            try {
                using (var sha = new SHA256Managed()) {
                    data = sha.ComputeHash(data);
                }
                return data;
            } catch {
                throw new FipsException();
            }
        }


        private static List<Album> ReadInfo(Stream stream) {
            var flag = stream.ReadString();
            var List = new List<Album>();
            if (flag != NPK_FlAG) {
                return List;
            }
            var count = stream.ReadInt();
            for (var i = 0; i < count; i++) {
                List.Add(new Album {
                    Offset = stream.ReadInt(),
                    Length = stream.ReadInt(),
                    Path = stream.ReadPath()
                });
            }
            return List;
        }

        /// <summary>
        ///     从NPK中获得img列表
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        public static List<Album> ReadNPK(this Stream stream, string file) {
            var List = new List<Album>();
            var flag = stream.ReadString();
            if (flag == NPK_FlAG) {
                //当文件是NPK时
                stream.Seek(0, SeekOrigin.Begin);
                List.AddRange(ReadInfo(stream));
                if (List.Count > 0) {
                    stream.Seek(32);
                }
            } else {
                var album = new Album();
                album.Path = file.GetSuffix();
                List.Add(album);
            }
            for (var i = 0; i < List.Count; i++) {
                var length = i < List.Count - 1 ? List[i + 1].Offset : stream.Length;
                stream.ReadImg(List[i], length);
            }
            return List;
        }

        public static void ReadImg(this Stream stream, Album album, long length) {
            stream.Seek(album.Offset, SeekOrigin.Begin);
            var albumFlag = stream.ReadString();
            if (albumFlag == IMG_FLAG) {
                album.IndexLength = stream.ReadLong();
                album.Version = (ImgVersion)stream.ReadInt();
                album.Count = stream.ReadInt();
                album.InitHandle(stream);
            } else  {
                if (albumFlag != IMAGE_FLAG) {
                    album.Version = ImgVersion.Other;
                    stream.Seek(album.Offset, SeekOrigin.Begin);
                    if (album.Name.ToLower().EndsWith(".ogg")) {
                        album.Version = ImgVersion.Other;
                        album.IndexLength = length - stream.Position;
                    }
                } else {
                    album.Version = ImgVersion.Ver1;
                }
                album.InitHandle(stream);
            }
        }


        /// <summary>
        ///     保存为NPK
        /// </summary>
        /// <param name="fileName"></param>
        public static void WriteNpk(this Stream stream, List<Album> List) {
            var position = 52 + List.Count * 264;
            for (var i = 0; i < List.Count; i++) {
                List[i].Adjust();
                if (i > 0) {
                    position += List[i - 1].Length;
                }
                List[i].Offset = position;
            }
            var ms = new MemoryStream();
            ms.WriteString(NPK_FlAG);
            ms.WriteInt(List.Count);
            foreach (var album in List) {
                ms.WriteInt(album.Offset);
                ms.WriteInt(album.Length);
                ms.WritePath(album.Path);
            }
            ms.Close();
            var data = ms.ToArray();
            stream.Write(data);
            stream.Write(CompileHash(data));
            foreach (var album in List) {
                stream.Write(album.Data);
            }
        }


        /// <summary>
        ///     根据已有的文件名获得img集合
        /// </summary>
        /// <param name="file"></param>
        /// <param name="names"></param>
        /// <returns></returns>
        public static List<Album> FindAll(string file, params string[] args) {
            var list = Load(file);
            list = new List<Album>(list.Where(item => args.Any(arg => item.Path.Contains(arg))));
            return list;
        }


        public static List<Album> Find(IEnumerable<Album> Items, params string[] args) {
            return Find(Items, false, args);
        }

        public static List<Album> Find(IEnumerable<Album> Items, bool allCheck, params string[] args) {
            var list = new List<Album>(Items.Where(item => {
                if (!allCheck && args.Length == 0) {
                    return true;
                }
                if (allCheck && !args[0].Equals(item.Name)) {
                    return false;
                }
                return args.All(arg => item.Path.Contains(arg));
            }));
            if (list.Count == 0) {
                list.AddRange(Items.Where(item => MatchCode(item.Name, args[0])));
            }
            return list;
        }

        public static List<Album> FindByCode(string path, int code) {
            return FindByCode(path, CompleteCode(code));
        }

        public static List<Album> FindByCode(string path, string code) {
            return FindByCode(path, code, false, false);
        }

        public static List<Album> FindByCode(string path, int code, bool mask, bool ban) {
            return FindByCode(path, CompleteCode(code), mask, ban);
        }

        public static List<Album> FindByCode(string path, string code, bool mask, bool ban) {
            var stream = File.OpenRead(path);
            var list = ReadInfo(stream);
            list = FindByCode(list, code, mask, ban);
            foreach (var al in list) {
                stream.Seek(al.Offset, SeekOrigin.Begin);
                stream.ReadImg(al, stream.Length);
            }
            stream.Close();
            var regex = new Regex("\\d+");
            list.ForEach(e => {
                e.TableIndex = int.Parse(code) % 100;
                e.Name = regex.Replace(e.Name, code, 1);
            });           
            return list;
        }

        public static List<Album> FindByCode(IEnumerable<Album> array, string code) {
            return FindByCode(array, code, false, false);
        }


        public static List<Album> FindByCode(IEnumerable<Album> array, string code, bool mask, bool ban) {
            var regex = new Regex("\\d+");
            var list = new List<Album>(array.Where(item => {
                if (!mask && item.Name.Contains("mask")) {
                    return false;
                }
                if (!ban && Regex.IsMatch(item.Name, @"\(.*\)+")) {
                    return false;
                }

                var match = regex.Match(item.Name);
                return match.Success && match.Value.Equals(code);
            }));
            if (list.Count == 0) {
                list.AddRange(array.Where(item =>  MatchCode(item.Name, code)));
            }
            return list;
        }


        public static List<Album> FindByCode(IEnumerable<Album> array, int codeNumber) {
            var code = CompleteCode(codeNumber);
            return FindByCode(array, code);
        }


        /// <summary>
        ///     v6 匹配规则
        /// </summary>
        /// <param name="name1"></param>
        /// <param name="name2"></param>
        /// <returns></returns>
        public static bool MatchCode(string name1, string name2) {
            var regex = new Regex("\\d+");
            var match0 = regex.Match(name1);
            var match1 = regex.Match(name2);
            if (match0.Success && match1.Success) {
                var code0 = int.Parse(match0.Value);
                var code1 = int.Parse(match1.Value);
                if (code0 == code1 || code0 == code1 / 100 * 100) {
                    return true;
                }
            }
            return false;
        }

        public static string CompleteCode(int code) {
            var str = code.ToString();
            if (code > -1) {
                while (str.Length < 4) {
                    str = string.Concat(0, str);
                }
            }
            return str;
        }

        /// <summary>
        ///     根据文件路径得到NPK名
        /// </summary>
        /// <param name="album"></param>
        /// <returns></returns>
        public static string GetFilePath(Album file) {
            var path = file.Path;
            var index = path.LastIndexOf("/");
            if (index > -1) {
                path = path.Substring(0, index);
            }
            path = path.Replace("/", "_");
            path += ".NPK";
            return path;
        }

        public static Album[] SplitFile(Album file) {
            var arr = new Album[Math.Max(1, file.Tables.Count)];
            var regex = new Regex("\\d+");
            var path = file.Name;
            var match = regex.Match(path);
            if (!match.Success) {
                return arr;
            }
            var prefix = path.Substring(0, match.Index);
            var suffix = path.Substring(match.Index + match.Length);
            var code = int.Parse(match.Value);
            file.Adjust();
            var data = file.Data;
            var ms = new MemoryStream(data);
            for (var i = 0; i < arr.Length; i++) {
                var name = prefix + CompleteCode(code + i) + suffix;
                arr[i] = ReadNPK(ms, file.Name)[0];
                arr[i].Path = file.Path.Replace(file.Name, name);
                arr[i].Tables.Clear();
                if (file.Tables.Count > 0) {
                    arr[i].Tables.Add(file.Tables[i]);
                }
                ms.Seek(0, SeekOrigin.Begin);
            }
            ms.Close();
            return arr;
        }

        #region 加载保存

        public static List<Album> Load(string file) {
            return Load(false, file);
        }

        public static List<Album> Load(bool onlyPath, string file) {
            var list = new List<Album>();
            if (Directory.Exists(file)) {
                return Load(onlyPath, Directory.GetFiles(file));
            }
            if (!File.Exists(file)) {
                return list;
            }
            using (var stream = File.OpenRead(file)) {
                if (onlyPath) {
                    return ReadInfo(stream);
                }
                var enums = stream.ReadNPK(file);
                return enums;
            }
        }

        public static List<Album> Load(bool onlyPath, params string[] files) {
            var List = new List<Album>();
            foreach (var file in files) {
                List.AddRange(Load(onlyPath, file));
            }
            return List;
        }

        public static List<Album> Load(params string[] files) {
            return Load(false, files);
        }


        public static Album LoadWithName(string file, string name) {
            using (var stream = File.OpenRead(file)) {
               return LoadWithName(stream, name);
            }
        }

        public static Album LoadWithName(Stream stream, string name) {
            var list = ReadInfo(stream);
            list = LoadWithNameArray(stream, name);
            if (list.Count > 0) {
                return list[0];
            }
            return null;
        }

        public static List<Album> LoadWithNameArray(Stream stream, params string[] names) {
            var list = ReadInfo(stream);
            list = list.FindAll(e => names.Contains(e.Path));
            foreach (var al in list) {
                stream.Seek(al.Offset, SeekOrigin.Begin);
                stream.ReadImg(al, stream.Length);
            }
            return list;
        }


        public static void Save(string file, List<Album> list) {
            using (var fs = File.Open(file, FileMode.Create)) {
                WriteNpk(fs, list);
            }
        }

        public static void SaveToDirectory(string dir, IEnumerable<Album> array) {
            foreach (var img in array) {
                img.Save($"{dir}/{img.Name}");
            }
        }

        #endregion

        #region 比较

        public static void Compare(string gamePath, Action<Album, Album> restore, params Album[] array) {
            Compare(gamePath, IMAGE_DIR, restore, array);
        }

        /// <summary>
        ///     与游戏原文件进行对比
        /// </summary>
        /// <param name="gamePath"></param>
        /// <param name="restore"></param>
        /// <param name="array"></param>
        public static void Compare(string gamePath, string dir, Action<Album, Album> restore, params Album[] array) {
            var dic = new Dictionary<string, List<string>>(); //将img按NPK分类
            foreach (var item in array) {
                var path = GetFilePath(item);
                path = $"{gamePath}/{dir}/{path}"; //得到游戏原文件路径
                if (!dic.ContainsKey(path)) {
                    dic.Add(path, new List<string>());
                }
                dic[path].Add(item.Name);
            }
            var list = new List<Album>();
            foreach (var item in dic.Keys) {
                list.AddRange(FindAll(item, dic[item].ToArray())); //读取游戏原文件
            }
            foreach(var a2 in array) { //模型文件
                foreach (var a1 in list) { //游戏原文件
                    if (a2.Path.Equals(a1.Path)) {
                        restore.Invoke(a1, a2);
                    }
                }
            }
        }

        #endregion
    }
}