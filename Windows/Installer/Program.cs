using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace CameraDockInstaller {
    static class Install {
        public static string Validate(string root) {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if(!File.Exists(Path.Combine(root,@"bin\64bit\obs64.exe"))) throw new IOException("Vyberte kořenovou složku OBS Studio obsahující bin\\64bit\\obs64.exe.");
            string target=Path.GetFullPath(Path.Combine(root,"obs-camera-dock"));
            // Never follow junctions/symlinks when installing or cleaning an elevated directory.
            for(var dir=new DirectoryInfo(target);dir!=null;dir=dir.Parent)
                if(dir.Exists&&(dir.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Instalace přes odkaz nebo junction není podporována: "+dir.FullName);
            if(Directory.Exists(target)) CheckTree(target,target);
            return target;
        }
        static void CheckTree(string directory,string target) {
            foreach(string entry in Directory.GetFileSystemEntries(directory)) {
                string full=Path.GetFullPath(entry);
                if(!full.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Soubor mimo cílovou složku.");
                var attributes=File.GetAttributes(full);
                if((attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Cílová složka obsahuje odkaz: "+entry);
                if((attributes&FileAttributes.Directory)!=0) CheckTree(full,target);
            }
        }
        public static void Run(string root,byte[] payload) {
            string target=Validate(root),exe=Path.Combine(target,"OBS Camera Dock.exe");
            string self=Path.GetFullPath(Application.ExecutablePath);
            if(self.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Spusťte instalátor mimo cílovou složku obs-camera-dock.");
            foreach(var process in Process.GetProcessesByName("OBS Camera Dock")) using(process) {
                string path;
                try { path=process.MainModule.FileName; } catch { throw new IOException("Ukončete všechny spuštěné instance OBS Camera Dock a zkuste instalaci znovu."); }
                if(String.Equals(path,exe,StringComparison.OrdinalIgnoreCase)) throw new IOException("Nejprve ukončete OBS Camera Dock přes ikonu v liště. OBS Studio může zůstat spuštěné.");
            }
            Directory.CreateDirectory(target);
            string staging=Path.Combine(target,".install-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                File.WriteAllBytes(staging,payload);
                if(File.Exists(exe)) File.Replace(staging,exe,null); else File.Move(staging,exe);
                // Revalidate immediately before cleanup; only this dedicated child is managed.
                Validate(root);
                foreach(string entry in Directory.GetFileSystemEntries(target)) {
                    if(String.Equals(entry,exe,StringComparison.OrdinalIgnoreCase)) continue;
                    if(Directory.Exists(entry)) Directory.Delete(entry,true); else File.Delete(entry);
                }
            } finally { if(File.Exists(staging)) File.Delete(staging); }
        }
    }
    class InstallerForm : Form {
        readonly TextBox folder=new TextBox();
        readonly Button install=new Button();
        public InstallerForm() {
            Text="OBS Camera Dock 0.4.3 — instalace";ClientSize=new Size(590,245);FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            using(var stream=typeof(InstallerForm).Assembly.GetManifestResourceStream("camera.ico")) Icon=new Icon(stream);
            var title=new Label{Text="Instalace do OBS Studio",Left=20,Top=18,Width=540,Font=new Font(SystemFonts.MessageBoxFont.FontFamily,14,FontStyle.Bold)};
            var label=new Label{Text="Kořenová složka OBS Studio:",Left=20,Top=60,Width=530};
            folder.SetBounds(20,86,440,26);folder.Text=FindObs();
            var browse=new Button{Text="Vybrat…",Left=470,Top=84,Width=100};
            browse.Click+=delegate { using(var dialog=new FolderBrowserDialog{Description="Vyberte složku OBS Studio",SelectedPath=folder.Text}) if(dialog.ShowDialog(this)==DialogResult.OK)folder.Text=dialog.SelectedPath; };
            var note=new Label{Left=20,Top=122,Width=550,Height=65,Text="V podsložce obs-camera-dock zůstane pouze OBS Camera Dock.exe.\nOstatní obsah této podsložky bude odstraněn. Soubory OBS Studio\na uložené presety v uživatelském profilu zůstanou zachované."};
            install.Text="Nainstalovat";install.SetBounds(420,198,150,30);
            install.Click+=delegate {
                install.Enabled=false;
                try {
                    byte[] payload;
                    using(var source=typeof(InstallerForm).Assembly.GetManifestResourceStream("payload.exe")) using(var memory=new MemoryStream()) {source.CopyTo(memory);payload=memory.ToArray();}
                    Install.Run(folder.Text,payload);
                    MessageBox.Show(this,"Hotovo. Aplikaci spustíte dvojklikem na:\n"+Path.Combine(folder.Text,@"obs-camera-dock\OBS Camera Dock.exe"),"Instalace dokončena",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    Close();
                } catch(Exception ex) {MessageBox.Show(this,ex.Message,"Instalace nebyla dokončena",MessageBoxButtons.OK,MessageBoxIcon.Error);install.Enabled=true;}
            };
            Controls.AddRange(new Control[]{title,label,folder,browse,note,install});
        }
        static string FindObs() {
            foreach(var process in Process.GetProcessesByName("obs64")) using(process) {
                try {string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(process.MainModule.FileName),@"..\.."));if(File.Exists(Path.Combine(root,@"bin\64bit\obs64.exe")))return root;}catch{}
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio");
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            if(args.Length==2&&args[0]=="--self-test") {
                try { Test(args[1]);return 0; }catch(Exception ex){File.WriteAllText(Path.Combine(args[1],"installer-test-error.txt"),ex.ToString());return 1;}
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new InstallerForm());return 0;
        }
        static void Test(string directory) {
            string root=Path.Combine(Path.GetFullPath(directory),"installer-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root,@"bin\64bit"));File.WriteAllText(Path.Combine(root,@"bin\64bit\obs64.exe"),"OBS sentinel");
            string target=Path.Combine(root,"obs-camera-dock");Directory.CreateDirectory(Path.Combine(target,"old"));File.WriteAllText(Path.Combine(target,@"old\readme.txt"),"old");
            File.WriteAllText(Path.Combine(root,"keep.txt"),"keep");
            var payload=new byte[]{1,2,3};Install.Run(root,payload);Install.Run(root,new byte[]{4,5});
            if(Directory.GetFileSystemEntries(target).Length!=1||File.ReadAllBytes(Path.Combine(target,"OBS Camera Dock.exe"))[0]!=4||File.ReadAllText(Path.Combine(root,"keep.txt"))!="keep"||File.ReadAllText(Path.Combine(root,@"bin\64bit\obs64.exe"))!="OBS sentinel")throw new Exception("Install/cleanup boundary test failed");
            bool rejected=false;try{Install.Run(target,payload);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Invalid root accepted");
            File.WriteAllText(Path.Combine(directory,"installer-tests-passed.txt"),"Install, replacement, cleanup and root validation passed.");
        }
    }
}
