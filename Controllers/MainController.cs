using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Reflection.Metadata;
using System.Xml.Linq;
using static System.Net.WebRequestMethods;
using System.Linq.Expressions;
using System;
using System.Text.Json;
using System.IO;
using static MonsterSiren.Controllers.MainController;
using System.Security.Cryptography;
using TagLib;
using System.Collections.Generic;
using System.IO.Pipes;
using static TagLib.File;
using TagLib.Mpeg;
using TagLib.Aac;
using TagLib.Aiff;
using TagLib.Ape;
using TagLib.Asf;
using System.Drawing;
using TagLib.Audible;
using TagLib.Ogg;
using System.Linq;
using System.Text;
using System.Net.Http;

namespace MonsterSiren.Controllers
{
    [Route("/")]
    [ApiController]
    public class MainController : ControllerBase
    {

        public static string curdir = System.IO.Directory.GetCurrentDirectory();

        [HttpOptions]
        public IActionResult Get()
        {
            return auth(Request, Response);
        }

        public static IActionResult auth(HttpRequest re,HttpResponse ro)
        {
            var username = CustomConfig.GetValue("login");
            var password = CustomConfig.GetValue("password");
            string encoded = System.Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

            if (re.Headers.Authorization.ToString() == "Basic "+ encoded)
            {
                Console.WriteLine("Successful authorization");
                ro.Headers.Add("Allow", "OPTIONS, LOCK, PROPPATCH, COPY, MOVE, UNLOCK, PROPFIND");
                ro.Headers.Add("Dav", "1, 2");
                ro.Headers.Add("Ms-Author-Via", "DAV");
                return new ContentResult
                {
                    StatusCode = 200
                };
            }
            else
            {
                ro.Headers.Add("Www-Authenticate", "Basic realm=dave");
                return new ContentResult
                {
                    Content = "401 Unauthorized",
                    StatusCode = 401
                };
            }
        }

        public class Asongs
        {
            public int Cid { get; set; }
            public string Name { get; set; }
            public List<string>? Artistes { get; set; }
            public string? SourceUrl { get; set; }
            public string? LyricUrl { get; set; }
            public string? MvUrl { get; set; }
            public string? MvCoverUrl { get; set; }
            public long? size { get; set; } = -1;
            public string? data { get; set; }
            public string? type { get; set; }
            public string? etag { get; set; }
        }

        public class Albums
        {
            public int Cid { get; set; }
            public string Name { get; set; }
            public List<string>? Artistes { get; set; }
            public string? CoverUrl { get; set; }
            public string? CoverDeUrl { get; set; }
            public string? Belong { get; set; }
            public string? Intro { get; set; }
            public List<Asongs> Songs { get; set; } = new();
        }

        public static List<Albums> albums = new List<Albums>();

        public async Task<IActionResult> Propfind()
        {
            StringValues Depth = "";
            Request.Headers.TryGetValue("Depth", out Depth);

            switch (Depth)
            {
                case "0":
                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml(),
                        StatusCode = 207
                    };
                    break;
                case "1":
                    Precheck(true);

                    List<XElement> DirList = new List<XElement>();

                    foreach (var item in albums)
                    {
                        DirList.Add(dirblock(item.Name));
                    }

                    Console.WriteLine("sent: \"");

                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml("/", DirList),
                        StatusCode = 207
                    };
                    break;
                default:
                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml(),
                        StatusCode = 207
                    };
                    break;
            }
        }

        [HttpOptions("{dirname}")]
        public IActionResult GetDir(string dirname)
        {
            return auth(Request, Response);
        }

        [AcceptVerbs("PROPFIND")]
        [Route("{dirname}")]
        public IActionResult PropfindChild(string dirname)
        {
            StringValues Depth = "";
            Request.Headers.TryGetValue("Depth", out Depth);
            //Console.WriteLine("Depth: " + Depth);

            switch (Depth)
            {
                case "0":
                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml("/" + dirname + "/"),
                        StatusCode = 207
                    };
                    break;
                case "1":
                    Precheck(true);

                    if (albums.FindAll(x => x.Name == dirname).Count == 0)
                    {
                        UpdateAlbum();
                        if (albums.FindAll(x => x.Name == dirname).Count == 0) return NotFound();

                        if (albums.FindAll(x => x.Name == dirname)[0].Songs.Count == 0)
                            UpdateSong(albums.FindAll(x => x.Name == dirname)[0].Cid);
                    }
                    else
                    {
                        if (albums.FindAll(x => x.Name == dirname)[0].Songs.Count == 0)
                            UpdateSong(albums.FindAll(x => x.Name == dirname)[0].Cid);
                    }

                    List<XElement> DirList = new List<XElement>();

                    foreach (var item in albums.FindAll(x => x.Name == dirname)[0].Songs)
                    {
                        DirList.Add(audioblock(item.Name, item.size.ToString(), item.data, item.type, item.etag, "/" + dirname + "/"));
                    }

                    Console.WriteLine("sent: \"/" + dirname + "/\"");

                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml("/" + dirname + "/", DirList),
                        StatusCode = 207
                    };
                    break;
                default:
                    return new ContentResult
                    {
                        ContentType = "text/xml; charset=utf-8",
                        Content = BaseXml("/" + dirname + "/"),
                        StatusCode = 207
                    };
                    break;
            }
        }

        public static void Write(string name, object value)
        {
            Console.WriteLine($"{name,20}: {value ?? ""}");
        }

        public static void Write(string name, string[] values)
        {
            Console.WriteLine($"{name,20}: {(values == null ? "" : string.Join("\n            ", values))}");
        }

        [HttpOptions("{dirname}/{filename}")]
        public IActionResult GetFileDir(string dirname, string filename)
        {
            return auth(Request, Response);
        }

        [HttpGet]
        [Route("{dirname}/{filename}")]
        public IActionResult GetFilesAsync(string dirname, string filename)
        {
            Asongs song;
            Albums album;

            List<Albums> if1 = albums.FindAll(x => x.Name == dirname);

            Precheck(true);

            /*if (if1.Count != 0)
            {
                List<Asongs> if2 = if1[0].Songs.FindAll(y => y.Name == filename);
                if (if2.Count != 0)
                {
                    album = if1[0];
                    song = if2[0];
                } else
                {
                    UpdateSong(albums.FindAll(x => x.Name == dirname)[0].Cid);
                    album = albums.FindAll(x => x.Name == dirname)[0];
                    song = albums.FindAll(x => x.Name == dirname)[0].Songs.FindAll(y => y.Name == filename)[0];
                }
            } else
            {
                if (albums.FindAll(x => x.Name == dirname).Count == 0) return NotFound();
                UpdateAlbum();
                UpdateSong(albums.FindAll(x => x.Name == dirname)[0].Cid);
                album = albums.FindAll(x => x.Name == dirname)[0];
                song = albums.FindAll(x => x.Name == dirname)[0].Songs.FindAll(y => y.Name == filename)[0];
            }*/

            if (albums.FindAll(x => x.Name == dirname).Count == 0)
            {
                UpdateAlbum();
                if (albums.FindAll(x => x.Name == dirname).Count == 0) return NotFound();
            }

            album = albums.FindAll(x => x.Name == dirname)[0];
            if (albums.FindAll(x => x.Name == dirname)[0].Songs.Count == 0)
                UpdateSong(albums.FindAll(x => x.Name == dirname)[0].Cid);
            if (albums.FindAll(x => x.Name == dirname)[0].Songs.Count == 0) return NotFound();
            if (albums.FindAll(x => x.Name == dirname)[0].Songs.FindAll(y => y.Name == filename).Count != 0)
            {
                song = albums.FindAll(x => x.Name == dirname)[0].Songs.FindAll(y => y.Name == filename)[0];
            }
            else
            {
                return NotFound();
            }

            Console.WriteLine("Start \""+ filename +"\" processing...");
            byte[] imageBytes;

            using (var client = new HttpClient())
            {
                var imageTask = client.GetStreamAsync(albums.FindAll(x => x.Name == dirname)[0].CoverUrl);
                imageTask.Wait();
                using (var memoryStream = new MemoryStream())
                {
                    imageTask.Result.CopyTo(memoryStream);
                    imageBytes = memoryStream.ToArray();
                }
            }

            try
            {
                TagLib.File AudioFile;

                using (var client = new HttpClient())
                {
                    var Streamfile = client.GetStreamAsync(song.SourceUrl);
                    Streamfile.Wait();
                    Stream file = Streamfile.Result;

                    Stream stream = new MemoryStream();
                    file.CopyTo(stream);

                    AudioFile = TagLib.File.Create(new FileAbstraction(song.SourceUrl, stream));

                    
                    AudioFile.Tag.Title = Path.GetFileNameWithoutExtension(song.Name);
                    AudioFile.Tag.Album = album.Name;
                    AudioFile.Tag.Comment = album.Intro;
                    AudioFile.Tag.Performers = song.Artistes.ToArray();
                    AudioFile.Tag.AlbumArtists = album.Artistes.ToArray();
                    AudioFile.Tag.Copyright = album.Belong;

                    if (song.LyricUrl != null)
                    {
                        var getstring = client.GetStringAsync(song.LyricUrl);
                        getstring.Wait();
                        AudioFile.Tag.Lyrics = getstring.Result;
                    }

                    Console.WriteLine($"Tags on disk:   {AudioFile.TagTypesOnDisk}");
                    Console.WriteLine($"Tags in object: {AudioFile.TagTypes}");
                    Console.WriteLine();

                    Write("Title", AudioFile.Tag.Title);
                    Write("Album Artists", AudioFile.Tag.AlbumArtists);
                    Write("Performers", AudioFile.Tag.Performers);
                    //Write("Composers", AudioFile.Tag.Composers);
                    //Write("Conductor", AudioFile.Tag.Conductor);
                    Write("Album", AudioFile.Tag.Album);
                    Write("Comment", AudioFile.Tag.Comment);
                    Write("Copyright", AudioFile.Tag.Copyright);
                    //Write("BPM", AudioFile.Tag.BeatsPerMinute);
                    Write("Year", AudioFile.Tag.Year);
                    //Write("Track", AudioFile.Tag.Track);
                    //Write("TrackCount", AudioFile.Tag.TrackCount);
                    //Write("Disc", AudioFile.Tag.Disc);
                    //Write("DiscCount", AudioFile.Tag.DiscCount);

                    Console.WriteLine($"Lyrics:\n{AudioFile.Tag.Lyrics}\n");

                    TagLib.Id3v2.AttachedPictureFrame pic = new TagLib.Id3v2.AttachedPictureFrame();
                    pic.TextEncoding = TagLib.StringType.Latin1;
                    pic.MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg;
                    pic.Type = TagLib.PictureType.FrontCover;
                    pic.Data = imageBytes;

                    // save picture to file
                    AudioFile.Tag.Pictures = new TagLib.IPicture[1] { pic };
                    AudioFile.Save();

                    /*AudioFile.Tag.Pictures = new TagLib.Picture[]
                        {
                        new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            Description = "Cover",
                            MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg
                        }
                        };*/
                    //return null;
                    //Console.Write(AudioFile.FileAbstraction.WriteStream.ToString());
                    //Console.Write(AudioFile.FileAbstraction.WriteStream.ToString());

                    //Stream outstream = new MemoryStream();
                    //AudioFile.FileAbstraction.CloseStream
                    //AudioFile.FileAbstraction.WriteStream.CopyTo(outstream);

                    //CopyStream(AudioFile.FileAbstraction.ReadStream, outstream);

                    //AudioFile.FileAbstraction.ReadStream.CopyTo(outstream);
                    //byte[] bbb;

                    //var memoryStream = new MemoryStream();

                    //.CopyTo(memoryStream);
                    //bbb = memoryStream.ToArray();

                    //return new FileStreamResult(outstream, song.type);
                    Console.WriteLine("audio processing completed!");
                    return new FileStreamResult(AudioFile.FileAbstraction.WriteStream, song.type);
                }
            }
            catch (UnsupportedFormatException)
            {
                Console.WriteLine($"UNSUPPORTED FILE: {song.SourceUrl}");
                Console.WriteLine();
                Console.WriteLine("---------------------------------------");
                Console.WriteLine();
            }

            return NotFound();
            /*using (var client = new HttpClient())
            {
                var Streamfile =  client.GetStreamAsync(song.SourceUrl);
                Streamfile.Wait();
                Stream file = Streamfile.Result;

                StreamFileAbstraction file2 = new StreamFileAbstraction(filename, file);
                TagLib.File AudioFile = TagLib.File.Create(file2);

                AudioFile.Tag.Pictures = new TagLib.Picture[]
                {
                    new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg
                    }
                };
                return null;
                //return new FileStreamResult(AudioFile.FileAbstraction.ReadStream, song.type);
            }*/
        }

        public class SongData
        {
            public string cid { get; set; }
            public string name { get; set; }
            public string intro { get; set; }
            public string belong { get; set; }
            public string coverUrl { get; set; }
            public string coverDeUrl { get; set; }
            public List<Song> songs { get; set; }
        }

        public class SongRoot
        {
            public int code { get; set; }
            public string msg { get; set; }
            public SongData data { get; set; }
        }

        public class Song
        {
            public string cid { get; set; }
            public string name { get; set; }
            public List<string> artistes { get; set; }
        }

        public class AudioFilData
        {
            public string cid { get; set; }
            public string name { get; set; }
            public string albumCid { get; set; }
            public string sourceUrl { get; set; }
            public string lyricUrl { get; set; }
            public string mvUrl { get; set; }
            public string mvCoverUrl { get; set; }
            public List<string> artists { get; set; }
        }

        public class AudioFileRoot
        {
            public int code { get; set; }
            public string msg { get; set; }
            public AudioFilData data { get; set; }
        }

        public static void UpdateSong(int cid)
        {
            Console.WriteLine("Update Song #"+cid);
            using (var httpClient = new HttpClient())
            {
                var getstring = httpClient.GetStringAsync("https://monster-siren.hypergryph.com/api/album/" + cid + "/detail");
                getstring.Wait();
                string jsonin = getstring.Result;

                var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var OrigList = JsonSerializer.Deserialize<SongRoot>(jsonin, options);

                albums.FindAll(x => x.Cid == cid)[0].Songs.Clear();

                foreach (var item in OrigList.data.songs)
                {
                    var getaudiostring = httpClient.GetStringAsync("https://monster-siren.hypergryph.com/api/song/" + item.cid);
                    getstring.Wait();
                    string jsonaudioin = getaudiostring.Result;
                    var OrigaudioList = JsonSerializer.Deserialize<AudioFileRoot>(jsonaudioin, options);

                    long size = -1;
                    string data = "";
                    string type = "";
                    string etag = "";
                    WebRequest req = WebRequest.Create(OrigaudioList.data.sourceUrl);
                    req.Method = "HEAD";

                    using (System.Net.WebResponse resp = req.GetResponse())
                    {
                        if (long.TryParse(resp.Headers.Get("Content-Length"), out long ContentLength))
                            size = ContentLength;
                        data = resp.Headers.Get("Last-Modified");
                        type = resp.Headers.Get("Content-Type");
                        etag = resp.Headers.Get("ETag").Split('"')[1];
                    }
                    
                    albums.FindAll(x => x.Cid == cid)[0].Songs.Add(new Asongs()
                    {
                        Cid = int.Parse(item.cid),
                        Name = item.name + "." + OrigaudioList.data.sourceUrl.Split('.').Last(),
                        Artistes = item.artistes,
                        SourceUrl = OrigaudioList.data.sourceUrl,
                        LyricUrl = OrigaudioList.data.lyricUrl,
                        MvUrl = OrigaudioList.data.mvUrl,
                        MvCoverUrl = OrigaudioList.data.mvCoverUrl,
                        size = size,
                        data = data,
                        type = type,
                        etag = etag
                    });     
                }

                albums.FindAll(x => x.Cid == cid)[0].Intro = OrigList.data.intro;
                albums.FindAll(x => x.Cid == cid)[0].Belong = OrigList.data.belong;
                albums.FindAll(x => x.Cid == cid)[0].CoverDeUrl = OrigList.data.coverDeUrl;

                string json = JsonSerializer.Serialize(albums);
                System.IO.File.WriteAllText(Path.Combine(curdir, "Albums.json"), json);
                Console.WriteLine("Update Song Finished!");
            }
        }

        public static void LoadJson()
        {
            string jsonin = System.IO.File.ReadAllText(Path.Combine(curdir, "Albums.json"));

            var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            albums = JsonSerializer.Deserialize<List<Albums>>(jsonin.ToString());
        }

        public class OrigDatum
        {
            public string cid { get; set; }
            public string name { get; set; }
            public string coverUrl { get; set; }
            public List<string> artistes { get; set; }
        }

        public class OrigRoot
        {
            public int code { get; set; }
            public string msg { get; set; }
            public List<OrigDatum> data { get; set; }
        }

        public static void UpdateAlbum()
        {
            Console.WriteLine("Update Album");

            string link = "https://monster-siren.hypergryph.com/api/albums";

            using (var httpClient = new HttpClient())
            {
                var getstring = httpClient.GetStringAsync(link);
                getstring.Wait();
                string jsonin = getstring.Result;

                var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var OrigList = JsonSerializer.Deserialize<OrigRoot>(jsonin, options);

                albums.Clear();

                foreach (var item in OrigList.data)
                {
                    albums.Add(new Albums()
                    {
                        Cid = int.Parse(item.cid),
                        Name = item.name,
                        Artistes = item.artistes,
                        CoverUrl = item.coverUrl
                    });
                }

                string json = JsonSerializer.Serialize(albums);
                System.IO.File.WriteAllText(Path.Combine(curdir, "Albums.json"), json);
            }
            Console.WriteLine("Update Album Finished!");
        }

        public static void Precheck(bool checktime = false)
        {
            if (albums.Count == 0)
            {
                if (System.IO.File.Exists(Path.Combine(curdir, "Albums.json")))
                {
                    if (checktime && IsBelowThreshold(Path.Combine(curdir, "Albums.json"), int.Parse(CustomConfig.GetValue("hours"))))
                    {
                        UpdateAlbum();
                    }
                    else
                    {
                        LoadJson();
                    }
                }
                else
                {
                    UpdateAlbum();
                }        
            }
            else
            {
                if (System.IO.File.Exists(Path.Combine(curdir, "Albums.json")))
                {
                    if (checktime && IsBelowThreshold(Path.Combine(curdir, "Albums.json"), int.Parse(CustomConfig.GetValue("hours"))))
                    {
                        UpdateAlbum();
                        //if (loadjson) LoadJson();
                    }
                }
                else
                {
                    string json = JsonSerializer.Serialize(albums);
                    System.IO.File.WriteAllText(Path.Combine(curdir, "Albums.json"), json);
                }
            }
        }

        public static bool IsBelowThreshold(string filename, int hours)
        {
            var threshold = DateTime.Now.AddHours(-hours);
            return System.IO.File.GetLastWriteTime(filename) <= threshold;
        }

        public static string BaseXml(string dir = "/", List<XElement> DirList = null)
        {
            XNamespace D = "DAV:";

            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + new XDocument
            (
                new XElement(D + "multistatus",
                    new XAttribute(XNamespace.Xmlns + "D", "DAV:"),
                    new XElement(D + "response",
                        new XElement(D + "href", Uri.EscapeUriString(dir)),
                        new XElement(D + "propstat",
                            new XElement(D + "prop",
                                new XElement(D + "resourcetype",
                                    new XElement(D + "collection", new XAttribute(XNamespace.Xmlns + "D", "DAV:"))
                                ),
                                new XElement(D + "displayname", ""),
                                new XElement(D + "getlastmodified", "Mon, 28 Nov 2022 14:50:11 GMT"),
                                new XElement(D + "supportedlock",
                                    new XElement(D + "lockentry", new XAttribute(XNamespace.Xmlns + "D", "DAV:"),
                                        new XElement(D + "lockscope",
                                            new XElement(D + "exclusive")
                                        ),
                                        new XElement(D + "locktype",
                                            new XElement(D + "write")
                                        )
                                    )
                                )
                            ),
                            new XElement(D + "status", "HTTP/1.1 200 OK")
                        )
                    ),
                    DirList
                )
            ).ToString();

        }

        public static XElement dirblock(string name, string dir = "/")
        {
            XNamespace D = "DAV:";

            return new XElement(D + "response",
                        new XElement(D + "href", Uri.EscapeUriString(dir) + Uri.EscapeUriString(name) + "/"),
                        new XElement(D + "propstat",
                            new XElement(D + "prop",
                                new XElement(D + "resourcetype",
                                    new XElement(D + "collection", new XAttribute(XNamespace.Xmlns + "D", "DAV:"))
                                ),
                                new XElement(D + "displayname", name),
                                new XElement(D + "getlastmodified", "Mon, 28 Nov 2022 14:50:11 GMT"),
                                new XElement(D + "supportedlock",
                                    new XElement(D + "lockentry", new XAttribute(XNamespace.Xmlns + "D", "DAV:"),
                                        new XElement(D + "lockscope",
                                            new XElement(D + "exclusive")
                                        ),
                                        new XElement(D + "locktype",
                                            new XElement(D + "write")
                                        )
                                    )
                                )
                            ),
                            new XElement(D + "status", "HTTP/1.1 200 OK")
                        )
                    );
        }

        public static XElement audioblock(string name, string size, string dage, string type, string etag, string dir = "/")
        {
            XNamespace D = "DAV:";

            return new XElement(D + "response",
                        new XElement(D + "href", Uri.EscapeUriString(dir) + Uri.EscapeUriString(name)),
                        new XElement(D + "propstat",
                            new XElement(D + "prop",
                                new XElement(D + "resourcetype"),
                                new XElement(D + "displayname", name),
                                new XElement(D + "getlastmodified", dage),
                                new XElement(D + "getcontenttype", type),
                                new XElement(D + "getcontentlength", size),
                                new XElement(D + "getetag", etag),
                                new XElement(D + "supportedlock",
                                    new XElement(D + "lockentry", new XAttribute(XNamespace.Xmlns + "D", "DAV:"),
                                        new XElement(D + "lockscope",
                                            new XElement(D + "exclusive")
                                        ),
                                        new XElement(D + "locktype",
                                            new XElement(D + "write")
                                        )
                                    )
                                )
                            ),
                            new XElement(D + "status", "HTTP/1.1 200 OK")
                        )
                    );
        }
    }
}
