using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NiconamaBbsGetter
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        protected override void OnLoad(EventArgs e)
        {
            txt_communityId.Text = Properties.Settings.Default.CommunityId;
            txt_Host.Text = Properties.Settings.Default.Host;
            txt_User.Text = Properties.Settings.Default.User;
            txt_Pass.Text = Properties.Settings.Default.Pass;
            txt_DbName.Text = Properties.Settings.Default.DbName;
            txt_CacheDir.Text = Properties.Settings.Default.CacheDir;
            base.OnLoad(e);
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            Properties.Settings.Default.CommunityId = txt_communityId.Text;
            Properties.Settings.Default.Host = txt_Host.Text;
            Properties.Settings.Default.User = txt_User.Text;
            Properties.Settings.Default.Pass = txt_Pass.Text;
            Properties.Settings.Default.DbName = txt_DbName.Text;
            Properties.Settings.Default.CacheDir = txt_CacheDir.Text;
            Properties.Settings.Default.Save();
            base.OnClosing(e);
        }
        private async void button1_Click(object sender, EventArgs e)
        {
            while (true)
            {
                var browser = ryu_s.BrowserCookie.BrowserManagerMaker.CreateInstance(ryu_s.MyCommon.Browser.BrowserType.Chrome);
                var cookies = browser.CookieGetter.GetCookieCollection("nicovideo.jp");
                var cc = new System.Net.CookieContainer();
                cc.Add(cookies);

                var settings = new NiconamaBbsGetter.Settings();
                settings.CacheDir = txt_CacheDir.Text;
                settings.Cc = cc;
                settings.CommunityId = txt_communityId.Text;
                settings.Host = txt_Host.Text;
                settings.User = txt_User.Text;
                settings.Pass = txt_Pass.Text;
                settings.DbName = txt_DbName.Text;

                var nicoBbs = new NiconamaBbsGetter.BbsGetter(settings);
                try {
                    await nicoBbs.Do();
                }catch(System.Net.WebException ex)
                {
                    ryu_s.MyCommon.Logging.LogException(ryu_s.MyCommon.LogLevel.error, ex);
                    Console.WriteLine(ex.Message);
                }
                Console.WriteLine("30秒待機");
                await Task.Delay(30 * 1000);
            }
        }
    }
}
