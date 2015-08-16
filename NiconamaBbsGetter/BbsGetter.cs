using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using MySql.Data.MySqlClient;
namespace NiconamaBbsGetter
{
    public class Res
    {
        public int Resnum;
        public string Name;
        public string Trip;
        public DateTime? Date;
        public string Id;
        public string Body;
        public string Oekaki;
    }
    public class Settings
    {
        public string CacheDir;
        public System.Net.CookieContainer Cc;
        public string CommunityId;
        public string Host;
        public string User;
        public string Pass;
        public string DbName;
    }
    public class Report
    {
        public string CurrentUrl;
    }
    public class BbsGetter
    {
        readonly string cacheDir;
        readonly System.Net.CookieContainer cc;
        readonly string communityId;

        readonly string host;
        readonly string username;
        readonly string password;
        readonly string dbName;

        //サーバにアクセスする間隔を開けるための待ち時間。
        const int waitTime = 5000;

        //1ページ辺りの最大レス数。
        const int ressPerPage = 30;

        string col_resnum = "resnum";
        string resnum_type_str = "int";
        MySqlDbType resnum_type = MySqlDbType.Int32;

        string col_name = "name";
        MySqlDbType name_type = MySqlDbType.VarChar;
        const int name_size = 100;
        string name_type_str = $"varchar({name_size})";

        string col_trip = "trip";
        MySqlDbType trip_type = MySqlDbType.VarChar;
        const int trip_size = 45;

        string col_id = "id";
        MySqlDbType id_type = MySqlDbType.VarChar;
        const int id_size = 45;

        string col_date = "date";
        MySqlDbType date_type = MySqlDbType.DateTime;

        string col_body = "body";
        MySqlDbType body_type = MySqlDbType.Text;

        string col_oekaki_url = "oekaki_url";
        MySqlDbType oekaki_url_type = MySqlDbType.VarChar;
        const int oekaki_url_size = 60;

        string col_oekaki_image = "oekaki_image";
        MySqlDbType oekaki_image_type = MySqlDbType.MediumBlob;

        public BbsGetter(Settings settings)
        {
            this.cacheDir = settings.CacheDir;
            this.cc = settings.Cc;
            this.communityId = settings.CommunityId;
            this.host = settings.Host;
            this.username = settings.User;
            this.password = settings.Pass;
            this.dbName = settings.DbName;

            if (!cacheDir.EndsWith(System.IO.Path.DirectorySeparatorChar + ""))
                cacheDir += System.IO.Path.DirectorySeparatorChar;
            this.cacheDir = cacheDir + communityId + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(cacheDir);
            Directory.CreateDirectory(this.cacheDir);


        }
        private void CreateTable(MySqlConnection conn)
        {
            //TODO:文字列に変数を埋め込む。バグを作らないために実際に使う時に実装しようと思う。
            var query = $"CREATE TABLE {communityId} ({col_resnum} int primary key, name varchar(100), trip varchar(45), id varchar(45) not null, date datetime, body text, oekaki_url varchar(60), oekaki_image mediumblob)";
            ryu_s.Db.MySQL.ExecuteNonQuery(conn, query);
        }
        public async Task Do(IProgress<Report> report)
        {
            await Task.Run(async () =>
            {
                await DoInternal(report);
            });
        }            
        public async Task DoInternal(IProgress<Report> report)
        { 
            var conn = ryu_s.Db.MySQL.CreateInstance(host, username, password, dbName);
            conn.Open();

            if (!await ryu_s.Db.MySQL.CheckIfTableExistsAsync(conn, dbName, communityId))
            {
                CreateTable(conn);
            }

            var localLatest = GetLocalLatestFilePath();
            string html = string.Empty;
            using (var sr = new System.IO.StreamReader(localLatest))
            {
                html = sr.ReadToEnd();
            }
            var list = ParseHtml(html);

            var query = await GetQuery();
            await ImageDownloader(list, query);

            foreach (var res in list)
            {
                await SaveToMysql(conn, res);
            }

            string nextUrl = "";
            var latestfilename = Path.GetFileNameWithoutExtension(localLatest);
            var latestUrl = ryu_s.MyCommon.Tool.Desanitize(latestfilename);
            if (list.Count < 30)
            {
                System.IO.File.Delete(localLatest);
                nextUrl = latestUrl;
            }
            else
            {
                var m = Regex.Match(latestUrl, $"http://dic.nicovideo.jp/b/c/{communityId}/(\\d+)-");
                if (m.Success)
                {
                    var latestResnum = int.Parse(m.Groups[1].Value);
                    nextUrl = GetUrl(latestResnum + ressPerPage);
                }
            }

            var headers = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string,string>("Accept-Language", "ja-JP"),
            };
loop:
            report.Report(new Report
            {
                CurrentUrl = nextUrl,
            });
            Console.WriteLine(nextUrl);
            var nextHtml = await GetHtml(nextUrl + query, headers);
            var nextList = ParseHtml(nextHtml);
            await ImageDownloader(nextList, query);
            foreach (var res in nextList)
            {
                await SaveToMysql(conn, res);
            }
            if (nextList.Count == ressPerPage)
            {
                var currentResnum = GetResnumFromUrl(nextUrl);
                nextUrl = GetUrl(currentResnum + ressPerPage);
                goto loop;
            }
            conn.Close();
        }
        private int GetResnumFromUrl(string url)
        {
            var m = Regex.Match(url, $"http://dic.nicovideo.jp/b/c/{communityId}/(\\d+)-");
            if (m.Success)
            {
                return int.Parse(m.Groups[1].Value);
            }
            throw new Exception($"URLが異常 URL={url}");
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="url">Query付きで</param>
        /// <param name="headers"></param>
        /// <returns></returns>
        private async Task<string> GetHtml(string url, KeyValuePair<string, string>[] headers)
        {
            var uri = new Uri(url);
            var urlWithoutQuery = url.Replace(uri.Query, "");
            var filePath = cacheDir + ryu_s.MyCommon.Tool.SanitizeForFilename(urlWithoutQuery) + ".txt";
            if (System.IO.File.Exists(filePath))
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(filePath))
                {
                    var s = sr.ReadToEnd();
                    return s;
                }
            }
            await Task.Delay(waitTime);
            string html = string.Empty;
            try
            {
                html = await ryu_s.Net.Http.GetAsync(url, headers, cc, Encoding.UTF8);
            }
            catch (System.Net.WebException ex)
            {
                throw ex;
            }
            finally
            {
                if (!System.IO.File.Exists(filePath))
                {
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filePath, false, Encoding.UTF8))
                    {
                        sw.Write(html);
                    }
                }
            }
            return html;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="resnum">(1 + 30 * n)の数値</param>
        /// <returns></returns>
        public string GetUrl(int resnum)
        {
            return $"http://dic.nicovideo.jp/b/c/{communityId}/{resnum}-";
        }
        public async Task<bool> ExistsOnMysql(MySqlConnection conn, Res res)
        {
            var query = $"select * from {communityId} where resnum={res.Resnum}";
            var reader = await ryu_s.Db.MySQL.ExecuteReaderAsync(conn, query);
            var dt = ryu_s.Db.MySQL.ConvertDataReader(reader);
            return (dt.Rows.Count > 0);
        }
        public async Task SaveToMysql(MySqlConnection conn, Res res)
        {
            if (await ExistsOnMysql(conn, res))
                return;
            byte[] image = null;
            if (res.Oekaki != null)
            {
                var imagePath = cacheDir + ryu_s.MyCommon.Tool.SanitizeForFilename(res.Oekaki);
                using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                {
                    using (var br = new BinaryReader(fs))
                    {
                        image = br.ReadBytes((int)fs.Length);
                    }
                }
            }
            var query = $"INSERT INTO {communityId} ({col_resnum},{col_name},{col_trip},{col_id},{col_date},{col_body},{col_oekaki_url},{col_oekaki_image})"
                + $"values(@{col_resnum},@{col_name},@{col_trip},@{col_id},@{col_date},@{col_body},@{col_oekaki_url},@{col_oekaki_image})";
            var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.Add($"@{col_resnum}", resnum_type);
            cmd.Parameters.Add($"@{col_name}", name_type, name_size);
            cmd.Parameters.Add($"@{col_trip}", trip_type, trip_size);
            cmd.Parameters.Add($"@{col_id}", id_type, id_size);
            cmd.Parameters.Add($"@{col_date}", date_type);
            cmd.Parameters.Add($"@{col_body}", body_type);
            cmd.Parameters.Add($"@{col_oekaki_url}", oekaki_url_type, oekaki_url_size);
            cmd.Parameters.Add($"@{col_oekaki_image}", oekaki_image_type);

            cmd.Parameters[$"@{col_resnum}"].Value = res.Resnum;
            cmd.Parameters[$"@{col_name}"].Value = (res.Name != null) ? MySqlHelper.EscapeString(res.Name) : res.Name;
            cmd.Parameters[$"@{col_trip}"].Value = res.Trip;
            cmd.Parameters[$"@{col_id}"].Value = res.Id;
            cmd.Parameters[$"@{col_date}"].Value = res.Date;
            cmd.Parameters[$"@{col_body}"].Value = (res.Body != null) ? MySqlHelper.EscapeString(res.Body) : res.Body;
            cmd.Parameters[$"@{col_oekaki_url}"].Value = res.Oekaki;
            cmd.Parameters[$"@{col_oekaki_image}"].Value = image;

            try
            {
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                ryu_s.MyCommon.Logging.LogException(ryu_s.MyCommon.LogLevel.error, ex);
            }
            return;
        }
        public async Task ImageDownloader(List<Res> list, string query)
        {
            foreach (var pngPath in list.Where(res => !string.IsNullOrWhiteSpace(res.Oekaki)).Select(res => res.Oekaki))
            {
                var filePath = cacheDir + ryu_s.MyCommon.Tool.SanitizeForFilename(pngPath);
                if (!System.IO.File.Exists(filePath))
                {
                    var headers2 = new KeyValuePair<string, string>[]
                    {
                        new KeyValuePair<string,string>("Accept", ".png,image/png"),
                    };
                    await Task.Delay(waitTime);
                    await ryu_s.Net.Http.GetImageAsync(pngPath + query, headers2, cc, filePath);
                }
            }
        }
        public List<Res> ParseHtml(string html)
        {
            var list = new List<Res>();
            //            var pattern = "<dt class=\"reshead\">\\s+<a name=\"\\d+\" class=\"resnumhead\"></a>(?<resnum>\\d+)\\s+：\\s+<span class=\"name\">(?<name>[^<]*?)</span>\\s+：(?<predate>.+?)\\s+ID: (?<id>[^\\s]+)\\s+</dt>\\s+<dd class=\"resbody\">\\n    (?<body>.+?)\\n    \\n(?<footer>.+?)</dd>";
            var pattern = "<dt class=\"reshead\">\\s+<a name=\"\\d+\" class=\"resnumhead\"></a>(?<resnum>\\d+)\\s+：\\s+(?<nametrip>.+?)\\s+：(?<predate>.+?)\\s+ID: (?<id>[^\\s]+)\\s+</dt>\\s+<dd class=\"resbody\">\\n    (?<body>.+?)\\n    \\n(?<footer>.+?)</dd>";
            //var pattern = "<dt class=\"reshead\">(?<res>.+?)</dd>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline | RegexOptions.Compiled);
            foreach (Match match1 in matches)
            {
                var res = new Res();

                var resnum = int.Parse(match1.Groups["resnum"].Value);
                res.Resnum = resnum;

                var nametrip = match1.Groups["nametrip"].Value;
                var namePattern = "<span class=\"name\">(?<name>.*?)</span>";
                var nameMatch = Regex.Match(nametrip, namePattern, RegexOptions.Compiled);
                if (nameMatch.Success)
                {
                    res.Name = nameMatch.Groups["name"].Value;
                }

                var tripPattern = "<span class=\"trip\">(?<trip>.*?)</span>";
                var tripMatch = Regex.Match(nametrip, tripPattern, RegexOptions.Compiled);
                if (tripMatch.Success)
                {
                    res.Trip = tripMatch.Groups["trip"].Value;
                }

                var predate = match1.Groups["predate"].Value;//"削除しました"の場合があるため一旦文字列として抽出し、再度正規表現にかける
                var datePattern = "(?<year>\\d+)/(?<month>\\d+)/(?<day>\\d+)\\(.\\) (?<hour>\\d+):(?<minute>\\d+):(?<second>\\d+)";
                var match = Regex.Match(predate, datePattern, RegexOptions.Compiled);
                if (match.Success)
                {
                    var year = int.Parse(match.Groups["year"].Value);
                    var month = int.Parse(match.Groups["month"].Value);
                    var day = int.Parse(match.Groups["day"].Value);
                    var hour = int.Parse(match.Groups["hour"].Value);
                    var minute = int.Parse(match.Groups["minute"].Value);
                    var second = int.Parse(match.Groups["second"].Value);
                    res.Date = new DateTime(year, month, day, hour, minute, second);
                }
                var id = match1.Groups["id"].Value;
                res.Id = id;

                var preBody = match1.Groups["body"].Value;
                res.Body = RemoveLinkFromBody(preBody);

                var footer = match1.Groups["footer"].Value;//お絵かきの画像のURLが含まれている。
                var oekakiPattern = "(http://dic\\.nicovideo\\.jp/.+\\.png)";
                var matchOekaki = Regex.Match(footer, oekakiPattern, RegexOptions.Singleline | RegexOptions.Compiled);
                if (matchOekaki.Success)
                {
                    res.Oekaki = matchOekaki.Groups[1].Value;
                }
                list.Add(res);
            }
            return list;
        }
        public string GetLocalLatestFilePath()
        {
            var pattern = $"http：／／dic\\.nicovideo\\.jp／b／c／{communityId}／(\\d+)-\\.txt";
            var files = Directory.GetFiles(cacheDir);
            var resnumList = new List<int>();
            foreach (var filename in files)
            {
                var match = Regex.Match(filename, pattern, RegexOptions.Compiled);
                if (match.Success)
                {
                    resnumList.Add(int.Parse(match.Groups[1].Value));
                }
            }
            string maxFilePath;
            if (resnumList.Count == 0)
            {
                //ファイルが無かった場合、空の擬似ファイルを作成する。
                var max = 1;
                maxFilePath = cacheDir + $"http：／／dic.nicovideo.jp／b／c／{communityId}／{max}-.txt";
                using (var sw = new StreamWriter(maxFilePath)) { }//空のファイルを作成
            }
            else
            {
                var max = resnumList.Max();
                maxFilePath = cacheDir + $"http：／／dic.nicovideo.jp／b／c／{communityId}／{max}-.txt";
            }
            //cacheDirにすでにキャッシュが存在し、なおかつMySQLを１から再構築する際にコメントアウトする。
//            maxFilePath = cacheDir + $"http：／／dic.nicovideo.jp／b／c／{communityId}／1-.txt";
            return maxFilePath;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="preBody"></param>
        /// <returns></returns>
        private static string RemoveLinkFromBody(string preBody)
        {
            var regex = new Regex("<a class=\"auto-hdn\" href=\"[^\"]+\" target=\"_blank\">(.+?)</a>", RegexOptions.Compiled);
            var tmp = preBody;
            tmp = regex.Replace(tmp, (m) =>
            {
                return m.Groups[1].Value;
            });
            var regex1 = new Regex("<a class=\"auto\" href=\"[^\"]+\" target=\"_blank\">(.+?)</a>", RegexOptions.Compiled);
            tmp = regex1.Replace(tmp, (m) =>
            {
                return m.Groups[1].Value;
            });
            var regex2 = new Regex("<a href=\".+?\" target=\"_blank\">(.+?)<img src=\".+?\" class=\"link-icon\"></a><br><iframe class=\"nicovideo\" frameborder=\"\\d+\" height=\"\\d+\" scrolling=\"no\" src=\".+?\" width=\"\\d+\">.+?</iframe>", RegexOptions.Compiled);
            tmp = regex2.Replace(tmp, (m) =>
            {
                return m.Groups[1].Value;
            });
            var regex3 = new Regex("<a href=[^>]+>(.+?)</a>", RegexOptions.Compiled);
            tmp = regex3.Replace(tmp, (m) =>
            {
                return m.Groups[1].Value;
            });
            var regex4 = new Regex("<wbr>(.*?)</wbr>", RegexOptions.Compiled);
            tmp = regex4.Replace(tmp, (m) =>
            {
                return m.Groups[1].Value;
            });
            var regex5 = new Regex("<img src=[^>]+>", RegexOptions.Compiled);
            tmp = regex5.Replace(tmp, (m) =>
            {
                return "";
            });

            return tmp;
        }
        private async Task<string> GetQuery()
        {
            var url = $"http://com.nicovideo.jp/bbs/{communityId}?side_bar=1";
            var headers = new[] {
                 new KeyValuePair<string,string>("Accept-Language", "ja-JP"),
            };
            var html = await ryu_s.Net.Http.GetAsync(url, headers, cc, Encoding.UTF8);
            var pattern = "<div><div><iframe src=\"(.+?)\" wi";
            var regex = new Regex(pattern);
            var match = regex.Match(html);
            if (match.Success)
            {
                var s = match.Groups[1].Value;
                var uri = new Uri(s);
                return uri.Query;
            }
            throw new Exception("仕様変更かも");
        }
    }
}
