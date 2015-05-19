using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Tool1
{
    class Program
    {
        static StreamWriter logSW = new StreamWriter(string.Format(ConfigurationManager.AppSettings["LogFileFormat"], DateTime.Now.ToString("yyy-MM-dd_HH.mm")), true);

        static string detailPageDomain = "http://www.cnki.net/KCMS";
        static string pdfDownloadUrlFormat = "http://epub.cnki.net/grid2008/docdown/docdownload.aspx?filename={0}&dbcode={1}&dflag=pdfdown";
        static string cnkiSearchUrlFormat = "http://search.cnki.net/Search.aspx?q={0}";
        static string cnkiSearchHandlerFormat = "http://epub.cnki.net/KNS/request/SearchHandler.ashx?action=&NaviCode=*&ua=1.13&PageName=ASP.brief_default_result_aspx&DbPrefix=SCDB&DbCatalog=%E4%B8%AD%E5%9B%BD%E5%AD%A6%E6%9C%AF%E6%96%87%E7%8C%AE%E7%BD%91%E7%BB%9C%E5%87%BA%E7%89%88%E6%80%BB%E5%BA%93&ConfigFile=SCDBINDEX.xml&db_opt=CJFQ%2CCJFN%2CCDFD%2CCMFD%2CCPFD%2CIPFD%2CCCND%2CCCJD%2CHBRD&txt_1_sel=FT%24%25%3D%7C&txt_1_value1={0}&txt_1_special1=%25&his=0&parentdb=SCDB&__=Thu%20Apr%2023%202015%2017%3A02%3A59%20GMT%2B0800%20(China%20Standard%20Time)";
        static string cnkiListPageUrl = "http://epub.cnki.net/kns/brief/brief.aspx?pagename=ASP.brief_default_result_aspx&dbPrefix=SCDB&dbCatalog=%E4%B8%AD%E5%9B%BD%E5%AD%A6%E6%9C%AF%E6%96%87%E7%8C%AE%E7%BD%91%E7%BB%9C%E5%87%BA%E7%89%88%E6%80%BB%E5%BA%93&ConfigFile=SCDBINDEX.xml&research=off&curpage=1&RecordsPerPage=" + ConfigurationManager.AppSettings["Recordperpage"];
        static string queryListFilePath = ConfigurationManager.AppSettings["QueryListFilePath"];
        static string serpOutputFormat = ConfigurationManager.AppSettings["SerpOutputFormat"];
        static string landingPageOutputFolder = ConfigurationManager.AppSettings["LandingPageOutputFolder"];
        static string pdfOutputFolder = ConfigurationManager.AppSettings["PDFOutputFolder"];
        static string resultFilePath = ConfigurationManager.AppSettings["ResultFilePath"];

        static void Main(string[] args)
        {
            try
            {
                // Get query list
                Dictionary<int, string> queryDic = new Dictionary<int, string>();
                using (StreamReader sr = new StreamReader(queryListFilePath))
                {
                    string line = string.Empty;
                    while (!string.IsNullOrEmpty(line = sr.ReadLine()))
                    {
                        string[] columns = line.Split('\t');
                        queryDic.Add(int.Parse(columns[0]), columns[1]);
                    }
                }

                #region 1. Scrape and save CNKI SERPs.
                WriteLog("Start scraping serp");
                foreach (KeyValuePair<int, string> kvp in queryDic)
                {
                    WriteLog(string.Format("Request cnki search handler for query:{0}.{1}", kvp.Key, kvp.Value));
                    GetHtmlContent(string.Format(cnkiSearchHandlerFormat, HttpUtility.UrlEncode(kvp.Value)));
                    WriteLog(string.Format("Scrape query:{0}.{1}", kvp.Key, kvp.Value));
                    string content = GetHtmlContent(cnkiListPageUrl);
                    using (StreamWriter sw = new StreamWriter(string.Format(serpOutputFormat, kvp.Key)))
                    {
                        sw.Write(content);
                    }
                    WriteLog(string.Format("Scrape query:{0}.{1} done! SERP saved at {2}", kvp.Key, kvp.Value, string.Format(serpOutputFormat, kvp.Key)));
                }
                WriteLog("Scrape serp done!");
                #endregion

                #region 2. Parse landing page and scrape.
                WriteLog("Start parsing serp and landing page");
                //Create mapping file
                using (StreamWriter sw = new StreamWriter(resultFilePath, false))
                {
                    sw.WriteLine("QueryIndex\tQueryText\tSerpPath\tPosition\tLandingPagePath\tTitle\tPDFDownloadLink\tPDFFilePath\tOthers...");
                }

                foreach (KeyValuePair<int, string> kvp in queryDic)
                {
                    WriteLog(string.Format("Parse serp {0}", string.Format(serpOutputFormat, kvp.Key)));
                    string content = "";
                    using (StreamReader sr = new StreamReader(string.Format(serpOutputFormat, kvp.Key)))
                    {
                        content = sr.ReadToEnd();
                    }

                    // Parse results in SERP
                    HtmlNodeCollection titleNodes = GetNodes(content, ".//table[@class='GridTableContent']//tr/td/a[@class='fz14']");
                    int position = 1;
                    foreach (var titleNode in titleNodes)
                    {
                        WriteLog(string.Format("Parse result {0} in serp {1}", position, string.Format(serpOutputFormat, kvp.Key)));
                        string outputLine = "";

                        // Get result title and url
                        string title = titleNode.InnerHtml.Replace("<script language=\"javascript\">document.write(ReplaceChar1(ReplaceChar(ReplaceJiankuohao('", "")
                            .Replace("'))));</script>", "").Replace("<font class=Mark>", "").Replace("</font>", "");
                        string url = detailPageDomain + titleNode.Attributes["href"].Value.Replace("/kns", "");

                        // Get landing page content
                        string landingPageContent = GetHtmlContent(url);

                        // Save landing page
                        string landingPageFileName = string.Format("{0}_{1}_{2}_{3}.html", kvp.Key, kvp.Value, position, title);
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        {
                            landingPageFileName = landingPageFileName.Replace(c, '-');
                        }
                        string landingPageOutputPath = Path.Combine(landingPageOutputFolder, landingPageFileName);
                        using (StreamWriter sw = new StreamWriter(landingPageOutputPath))
                        {
                            sw.Write(landingPageContent);
                        }

                        outputLine = string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", kvp.Key, kvp.Value, string.Format(serpOutputFormat, kvp.Key), position, landingPageOutputPath, title);

                        // PDF download link
                        NameValueCollection nvc = HttpUtility.ParseQueryString(new Uri(url).Query);
                        string pdfDownloadLink = string.Format(pdfDownloadUrlFormat, nvc["FileName"], nvc["DbCode"]);
                        string pdfOutputPath = Path.Combine(pdfOutputFolder, landingPageFileName.Replace(".html", ".pdf"));
                        using (StreamWriter sw = new StreamWriter(pdfOutputPath, false, Encoding.UTF8))
                        {
                            sw.Write(GetHtmlContent(pdfDownloadLink, string.Format(cnkiSearchUrlFormat, HttpUtility.UrlEncode(kvp.Value))));
                        }
                        outputLine += "\t" + pdfDownloadLink + "\t" + pdfOutputPath;

                        // Parse landing page
                        HtmlNodeCollection nodes = GetNodes(landingPageContent, ".//div[@class='author summaryRight' or contains(@class,'summary')]/p | .//div[@class='keywords']");
                        foreach (var node in nodes)
                        {
                            // clean up tags
                            string text = Regex.Replace(node.InnerHtml.Replace(" ", "").Replace("\r\n", "").Replace("<br>", ""), "</?a[^>]*?>|</?span[^>]*?>", "");
                            outputLine += "\t" + text;
                        }

                        using (StreamWriter sw = new StreamWriter(resultFilePath, true))
                        {
                            sw.WriteLine(outputLine);
                        }

                        position++;
                    }
                }
                WriteLog("Parse serp and landing page done!");
                #endregion
            }
            catch (Exception e)
            {
                WriteLog(e.Message);
                WriteLog(e.StackTrace);
            }
        }

        private static HtmlNode GetSingleNode(string input, string xPath)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(input);
            return doc.DocumentNode.SelectSingleNode(xPath);
        }

        private static HtmlNodeCollection GetNodes(string input, string xPath)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(input);
            return doc.DocumentNode.SelectNodes(xPath);
        }

        private static string GetHtmlContent(string url, string referer = "")
        {
            int retry = 0;
            string content = "";
            while (++retry < 10)
            {
                try
                {
                    HttpWebRequest request = HttpWebRequest.Create(url) as HttpWebRequest;
                    request.Method = "GET";
                    request.UserAgent = "Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.3; WOW64; Trident/7.0; .NET4.0E; .NET4.0C; .NET CLR 3.5.30729; .NET CLR 2.0.50727; .NET CLR 3.0.30729; InfoPath.3; TU/1)";
                    request.CookieContainer = new CookieContainer();
                    Cookie c1 = new Cookie("ASP.NET_SessionId", "20syjn55vnwi2d55edo0rhv2");//this id may need update in the future
                    c1.Domain = new Uri(url).Host;
                    request.CookieContainer.Add(c1);

                    if (!string.IsNullOrEmpty(referer))
                    {
                        request.Referer = referer;
                    }

                    HttpWebResponse webResponse = request.GetResponse() as HttpWebResponse;
                    Encoding encoding = Encoding.UTF8;
                    if (!string.IsNullOrEmpty(webResponse.CharacterSet))
                    {
                        encoding = Encoding.GetEncoding(webResponse.CharacterSet);
                    }
                    using (StreamReader sr = new StreamReader(webResponse.GetResponseStream(), encoding))
                    {
                        content = sr.ReadToEnd();
                    }
                    retry = 10;
                }
                catch (Exception e)
                {
                    WriteLog("Error occure when processing url: " + url + "  retry:" + retry);
                }
            }

            if (string.IsNullOrEmpty(content))
            {
                throw new Exception("Fail to get html content, url: " + url);
            }
            return content;
        }

        public static void WriteLog(string log)
        {
            string logWithTime = DateTime.Now.ToLocalTime().ToString() + "---" + log;
            Console.WriteLine(logWithTime);
            logSW.WriteLine(logWithTime);
            logSW.Flush();
        }
    }
}
