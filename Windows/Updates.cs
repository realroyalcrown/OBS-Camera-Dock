using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Diagnostics;

namespace CameraDock {
    static class Updates {
        public const string Releases="https://github.com/realroyalcrown/OBS-Camera-Dock/releases";
        public static Version Current { get { return typeof(Program).Assembly.GetName().Version; } }
        public static string Label { get { return "Verze "+Current.ToString(3)+" · Windows preview"; } }
        public sealed class Release { public Version Version; public string Url; }
        public static Release Select(string content,Version current) {
            var json=new JavaScriptSerializer { MaxJsonLength=4*1024*1024 };
            var releases=json.Deserialize<List<Dictionary<string,object>>>(content);
            Release best=null;
            foreach(var release in releases) {
                if(J.Bool(release,"draft")) continue;
                // Windows assets distinguish this release channel from macOS releases.
                foreach(var asset in J.Items(release,"assets")) {
                    string name=J.String(asset,"name");
                    var match=Regex.Match(name,@"^OBS-Camera-Dock-Windows-(\d+)\.(\d+)\.(\d+)(?:-preview)?\.zip$",RegexOptions.IgnoreCase);
                    if(!match.Success) continue;
                    Version version;
                    if(!Version.TryParse(match.Groups[1]+"."+match.Groups[2]+"."+match.Groups[3]+".0",out version)) continue;
                    string url=J.String(asset,"browser_download_url");
                    Uri uri;
                    if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!String.IsNullOrEmpty(uri.UserInfo)||!uri.IsDefaultPort||!uri.AbsolutePath.StartsWith("/realroyalcrown/OBS-Camera-Dock/releases/download/",StringComparison.Ordinal)) continue;
                    if(version>current&&(best==null||version>best.Version)) best=new Release{Version=version,Url=url};
                }
            }
            return best;
        }
        public static async void Check(ToolStripItem item) {
            item.Enabled=false;
            try {
                Release release=await Task.Run(()=> {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var request=(HttpWebRequest)WebRequest.Create("https://api.github.com/repos/realroyalcrown/OBS-Camera-Dock/releases?per_page=100");
                    request.UserAgent="OBS-Camera-Dock/"+Current.ToString(3); request.Accept="application/vnd.github+json";
                    request.Timeout=15000;request.ReadWriteTimeout=15000;
                    using(var response=request.GetResponse()) using(var stream=response.GetResponseStream()) using(var reader=new System.IO.StreamReader(stream)) {
                        var text=new System.Text.StringBuilder(); var buffer=new char[8192];int count;
                        while((count=reader.Read(buffer,0,buffer.Length))>0) { text.Append(buffer,0,count);if(text.Length>4*1024*1024) throw new InvalidOperationException("Odpověď GitHubu je příliš velká."); }
                        return Select(text.ToString(),Current);
                    }
                });
                if(release==null) MessageBox.Show("Na GitHubu není publikovaný novější Windows balíček.","Aktualizace",MessageBoxButtons.OK,MessageBoxIcon.Information);
                else if(MessageBox.Show("Je dostupná verze "+release.Version.ToString(3)+". Otevřít stažení?\n\nRozbalte ZIP, ukončete stávající helper a spusťte nové EXE. Nastavení zůstane zachováno.","Aktualizace",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes) Process.Start(release.Url);
            } catch(Exception) {
                if(MessageBox.Show("Kontrola aktualizací se nezdařila. Otevřít stránku vydání na GitHubu?","Aktualizace",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes) Process.Start(Releases);
            } finally { if(!item.IsDisposed) item.Enabled=true; }
        }
    }
}
