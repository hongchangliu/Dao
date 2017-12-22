using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Newtonsoft.Json.Linq;
using JsonPath;
using Newtonsoft.Json;
using System.Xml.Linq;
using MySql.Data.MySqlClient;
using System.Configuration;
using System.Threading;
using System.Timers;

namespace Dao
{
    class Program
    {
        //首先我们必须要实例化一个Timer实例，
        static System.Timers.Timer timer = new System.Timers.Timer();
        //static System.Timers.Timer timer2 = new System.Timers.Timer();
        //增加状态锁，当时间事件和新增文件事件同时触发，只能执行一个，防止并发产生
        static bool processStatus = false;
        static List<string> batchNames = new List<string>();
        static string rootPath = Environment.CurrentDirectory + "\\PFacial";
        static string nousedFile = Environment.CurrentDirectory + "\\未生成xml的json.txt";
        static string usedFile = Environment.CurrentDirectory + "\\生成xml的json.txt";
        static void Main(string[] args)
        {
            Console.WriteLine("首次运行，请耐心等待");
            if (!Directory.Exists(rootPath))
            {
                string log = "PFacial 目录不存在。" + rootPath;
                Dao.DaoUtil.writeLog(log, 1);
                return;
            }

            List<FileInfo> fileList = Dao.DaoUtil.GetAllFiles(new System.IO.DirectoryInfo(rootPath));

            getBatchNameFromDB();
            if (batchNames.Count() == 0)
            {
                Dao.DaoUtil.writeLog("请配置数据库task表数据", 0);
                return;
            }
            //删除未生成json的文件
            FileInfo nousedFileInfo = new FileInfo(nousedFile);
            if (nousedFileInfo.Exists)
            {
                nousedFileInfo.Delete();
            }
            //删除已经生成xml的json
            FileInfo usedFileInfo = new FileInfo(usedFile);
            if (usedFileInfo.Exists)
            {
                usedFileInfo.Delete();
            }
            //抢占状态锁
            processStatus = true;
            try
            {
                StringBuilder nousedjson = new StringBuilder();
                foreach (var file in fileList)
                {
                    Dao.DaoUtil.writeLog("===============================", 0);
                    Dao.DaoUtil.writeLog("开始处理文件" + file.FullName, 0);

                    //通过数据库判断是否需要读取
                    if (!batchNames.Contains(file.Directory.Name))
                    {
                        //Dao.DaoUtil.writeLog(file.FullName.Replace(rootPath, ""), 2);
                        nousedjson.Append(file.FullName.Replace(rootPath, "") + "\n");
                        continue;
                    }
                    try
                    {
                        handleFile(file);
                    }
                    catch (Exception ex)
                    {
                        Dao.DaoUtil.writeLog("文件读取异常，跳过。" + file.FullName + "  " + ex.Message, 1);
                    }
                }
                if (nousedjson.Length > 0)
                {
                    Dao.DaoUtil.writeLog(nousedjson.ToString(), 2);
                }
            }
            catch (Exception ex)
            {
                Dao.DaoUtil.writeLog("文件读取异常，跳过。  " + ex.Message, 1);
            }
            finally
            {
                processStatus = false;
            }

            initTimer();
            WatcherStart(rootPath, "*.Completed");
            Console.WriteLine("==============================");
            Console.WriteLine("正在监控文件夹变化，请勿关闭窗口：" + rootPath);
            Console.Read();
        }

        private static void initTimer()
        {
            //把我们定义好的时间加到timer上去
            timer.Elapsed += new ElapsedEventHandler(timer_Elapsed);
            //设置timer的状态为启用           
            timer.Enabled = true;
            //设置timer的间隔时间           
            timer.Interval = 10 * 1000;

            //timer2.Elapsed += new ElapsedEventHandler(timer2_Elapsed);
            //timer2.Enabled = false;
            //timer2.Interval = 30 * 60 * 1000;
        }

        static void timer2_Elapsed(object sender, ElapsedEventArgs e)
        {
            while (processStatus)//如果状态锁被占用，需要等待五秒钟
            {
                Thread.Sleep(5 * 1000);
            }
            processStatus = true;//占有状态锁
            try
            {
                List<string> lines = new List<string>(File.ReadAllLines(usedFile));//先读取到内存变量

                List<FileInfo> fileInfos = Dao.DaoUtil.GetAllFiles(new DirectoryInfo(rootPath));
                foreach (FileInfo fi in fileInfos)
                {
                    for (int i = lines.Count - 1; i > 0; i--)
                    {
                        string line = lines[i];
                        if (line != null && line.Contains(fi.Name))
                        {
                            if (!line.Contains(fi.CreationTime.Ticks.ToString()))
                            {
                                handleFile(fi);
                                lines.RemoveAt(i);
                                File.WriteAllLines(usedFile, lines.ToArray());//在写回硬盤
                                continue;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Dao.DaoUtil.writeLog("检测文件是否最新异常:" + ex.Message, 1);
            }
            finally
            {
                processStatus = false;
            }
        }

        //读取新文件
        private static void OnCreated(object source, FileSystemEventArgs e)
        {
            Dao.DaoUtil.writeLog("文件新建事件处理逻辑 " + e.ChangeType + "  " + e.FullPath + "  " + e.Name + "", 0);
            while (processStatus)//如果状态锁被占用，需要等待五秒钟
            {
                Thread.Sleep(5 * 1000);
            }
            processStatus = true;//占有状态锁
            getBatchNameFromDB();

            FileInfo newFileInfo = new FileInfo(e.FullPath);

            if (batchNames.Count() == 0)
            {
                Dao.DaoUtil.writeLog("请配置数据库task表数据", 0);
                return;
            }
            if (!batchNames.Contains(newFileInfo.Directory.Name))
            {
                Dao.DaoUtil.writeLog(newFileInfo.FullName.Replace(rootPath, ""), 2);
                return;
            }
            try
            {
                handleFile(newFileInfo);
            }
            catch (Exception)
            {
                Dao.DaoUtil.writeLog("文件读取异常，跳过。" + newFileInfo.FullName, 1);
            }
            finally
            {
                processStatus = false;//释放状态锁
            }
        }


        private static void getBatchNameFromDB()
        {
            batchNames = new List<string>();
            string sql = ConfigurationManager.AppSettings["sql"].ToString();
            //开始访问数据库
            MySqlDataReader reader = Dao.DaoUtil.getmysqlread(sql);
            while (reader.Read())
            {
                string batchName = reader.GetString(0);
                if (null != batchName && !"".Equals(batchName))
                {
                    batchNames.Add(batchName);
                }
            }
            reader.Close();
        }
        //handle json file
        private static void handleFile(FileInfo file)
        {
            if (!file.Name.Contains("40146_b079845-00039.jpg.Completed"))
            {
                return;
            }
            string fileName = file.Name.Substring(0, file.Name.IndexOf("."));
            ////如果文件夹中已经生成了xml，则需要跳过
            //string xmlFileName = fileName + ".xml";
            //FileInfo xmlFile = new FileInfo(file.Directory + "\\" + xmlFileName);
            //if (xmlFile.Exists)
            //{
            //    Dao.DaoUtil.writeLog(xmlFileName + "已经生成过，故跳过生成", 0);
            //    return;
            //}
            Dao.DaoUtil.writeLog("开始读取：" + file.Name, 0);
            StreamReader sr = new StreamReader(file.FullName, Encoding.UTF8);
            String line;
            StringBuilder sBuilder = new StringBuilder();
            while ((line = sr.ReadLine()) != null)
            {
                sBuilder.Append(line);
            }


            //获取根节点对象
            XDocument document = new XDocument();
            XElement root = new XElement("StandardJob");
            root.SetAttributeValue("JobName", "48381");
            root.SetAttributeValue("BatchName", file.Directory.Name);
            root.SetAttributeValue("FileName", fileName);
            XElement stats = new XElement("Stats");
            XElement stat = new XElement("Stat");
            stat.SetAttributeValue("id", "1");
            stat.SetAttributeValue("UpdateTime", "2017-04-17 15:37:34");

            XElement Status = new XElement("Status");
            Status.SetValue("Key");
            stat.Add(Status);

            XElement Operator = new XElement("Operator");
            Operator.SetValue("B01953");
            stat.Add(Operator);

            XElement TemplateID = new XElement("TemplateID");
            TemplateID.SetValue("0");
            stat.Add(TemplateID);

            XElement ClientVersion = new XElement("ClientVersion");
            ClientVersion.SetValue("LiFT v6.0.17.0413A");
            stat.Add(ClientVersion);

            XElement Records = new XElement("Records");
            for (int i = 0; i < 2; i++)
            {
                XElement Element = new XElement("Element");
                Element.SetAttributeValue("id", i);
                Element.SetAttributeValue("type", "Table");
                Element.SetAttributeValue("rows", "1");
                Records.Add(Element);
            }

            stat.Add(Records);

            XElement KeyStrokes = new XElement("KeyStrokes");
            KeyStrokes.SetValue("59");
            stat.Add(KeyStrokes);

            stats.Add(stat);
            root.Add(stats);


            ////////////////////关键代码//////////////////////////////////
            string doctype = "";
            JObject json = JObject.Parse(sBuilder.ToString());
            var context = new JsonPathContext { ValueSystem = new JsonNetValueSystem() };
            try
            {
                doctype = context.SelectNodes(json, "$..doctype").Single().Value.ToString();
            }
            catch (Exception) { }

            XElement Groups = new XElement("Groups");
            XElement Group = new XElement("Group");
            XElement Elements = new XElement("Elements");
            Group.SetAttributeValue("id", "0");


            var values1 = context.SelectNodes(json, "$..faces");

            Dictionary<String, XElement> dic = new Dictionary<string, XElement>();
            var values = context.SelectNodes(json, "$..faces.*").Select(node => node.Value);
            XElement rows_body = null;
            XElement ele_body = null;
            int count = 0;
            if (values.Count() == 0)
            {
                //title
                XElement Rows_f = new XElement("Rows");
                XElement Row_f = new XElement("Row");
                Row_f.SetAttributeValue("id", "1");
                Row_f.Add(NewColumn("Comments", doctype, null));


                //Grade对应grade(如果grade为null,该xml栏位作空白处理)
                string grade = "";
                string xy_grade = "";

                if (!dic.Keys.Contains(grade))
                {
                    XElement Element_f = new XElement("Element");
                    Element_f.SetAttributeValue("id", ++count);
                    if (rows_body != null)
                    {
                        ele_body.Add(rows_body);
                        Elements.Add(ele_body);
                        rows_body = new XElement("Rows");
                    }
                    Row_f.Add(NewColumn("Grade", "null".Equals(grade) ? "" : grade, xy_grade));
                    Rows_f.Add(Row_f);
                    Element_f.Add(Rows_f);
                    Elements.Add(Element_f);

                    //body
                    ele_body = new XElement("Element");
                    ele_body.SetAttributeValue("id", ++count);
                    rows_body = new XElement("Rows");

                    dic[grade] = rows_body;
                }


                #region

                XElement Row = new XElement("Row");
                Row.SetAttributeValue("id", "1");

                Row.Add(NewColumn("Prefix", "", ""));

                Row.Add(NewColumn("Given", "", ""));

                Row.Add(NewColumn("Surname", "", ""));

                Row.Add(NewColumn("Suffix", "", ""));

                Row.Add(NewColumn("Facial_Coordinates", "", null));

                Row.Add(NewColumn("Box_Coordinates", "", null));


                Row.Add(NewColumn("Picture_Coordinates", "", null));
                #endregion
                rows_body.Add(Row);
            }
            else
            {
                //所有的faces values
                for (int i = 0; i < values.Count(); i++)
                {
                    //title
                    XElement Rows_f = new XElement("Rows");
                    XElement Row_f = new XElement("Row");
                    Row_f.SetAttributeValue("id", "1");
                    if (i == 0)
                    {
                        Row_f.Add(NewColumn("Comments", doctype, null));
                    }

                    //Grade对应grade(如果grade为null,该xml栏位作空白处理)
                    string grade = "";
                    try
                    {
                        grade = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates']['grade']['text']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string xy_grade = GetRectXY(json, context, i, "grade");

                    if (!dic.Keys.Contains(grade))
                    {
                        XElement Element_f = new XElement("Element");
                        Element_f.SetAttributeValue("id", ++count);
                        if (rows_body != null)
                        {
                            ele_body.Add(rows_body);
                            Elements.Add(ele_body);
                            rows_body = new XElement("Rows");
                        }
                        Row_f.Add(NewColumn("Grade", "null".Equals(grade) ? "" : grade, xy_grade));
                        Rows_f.Add(Row_f);
                        Element_f.Add(Rows_f);
                        Elements.Add(Element_f);

                        //body
                        ele_body = new XElement("Element");
                        ele_body.SetAttributeValue("id", ++count);
                        rows_body = new XElement("Rows");

                        dic[grade] = rows_body;
                    }


                    #region

                    XElement Row = new XElement("Row");
                    Row.SetAttributeValue("id", "1");
                    string prefix = "";
                    try
                    {
                        prefix = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates']['prefix']['text']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string xy_prefix = GetRectXY(json, context, i, "prefix");
                    Row.Add(NewColumn("Prefix", prefix, xy_prefix));

                    string given = "";
                    try
                    {
                        given = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates']['given']['text']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string xy_given = GetRectXY(json, context, i, "given");
                    Row.Add(NewColumn("Given", given, xy_given));

                    string surname = "";
                    try
                    {
                        surname = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates']['surname']['text']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string xy_surname = GetRectXY(json, context, i, "surname");
                    Row.Add(NewColumn("Surname", surname, xy_surname));

                    string suffix = "";
                    try
                    {
                        suffix = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates']['suffix']['text']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string xy_suffix = GetRectXY(json, context, i, "suffix");
                    Row.Add(NewColumn("Suffix", suffix, xy_suffix));


                    string x1, x2, x3, y1, y2, y3;
                    try
                    {
                        x1 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['leftEyeCenterX']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        x1 = "";
                    }
                    try
                    {
                        y1 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['leftEyeCenterY']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        y1 = "";
                    }
                    try
                    {
                        x2 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['rightEyeCenterX']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        x2 = "";
                    }

                    try
                    {
                        y2 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['rightEyeCenterY']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        y2 = "";
                    }
                    try
                    {
                        x3 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['chinTipX']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        x3 = "";
                    }
                    try
                    {
                        y3 = context.SelectNodes(json, "$['faces'][" + i + "]['metadata']['chinTipY']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                        y3 = "";
                    }
                    string facial = "";
                    if (!"".Equals(x1) && !"".Equals(x2) && !"".Equals(x3) && !"".Equals(y1) && !"".Equals(y2) && !"".Equals(y3))
                    {
                        facial = String.Format("{0},{1},{2},{3},{4},{5}", x1, y1, x2, y2, x3, y3);
                    }
                    Row.Add(NewColumn("Facial_Coordinates", facial, null));

                    try
                    {
                        x1 = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['left']").Single().Value.ToString();
                    }
                    catch (Exception) { }
                    try
                    {
                        y1 = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['top']").Single().Value.ToString();
                    }
                    catch (Exception) { }

                    string faceWidth = "";
                    try
                    {
                        faceWidth = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['width']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string faceLeft = "";
                    try
                    {
                        faceLeft = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['left']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string faceHeight = "";
                    try
                    {
                        faceHeight = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['height']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string faceTop = "";
                    try
                    {
                        faceTop = context.SelectNodes(json, "$['faces'][" + i + "]['face_boundingBox']['top']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    faceWidth = faceWidth == null || "".Equals(faceWidth.Trim()) ? "0" : faceWidth;
                    faceLeft = faceLeft == null || "".Equals(faceLeft.Trim()) ? "0" : faceLeft;
                    faceHeight = faceHeight == null || "".Equals(faceHeight.Trim()) ? "0" : faceHeight;
                    faceTop = faceTop == null || "".Equals(faceTop.Trim()) ? "0" : faceTop;

                    x2 = (double.Parse(faceWidth)
                        + double.Parse(faceLeft)) + "";
                    y2 = (double.Parse(faceHeight)
                        + double.Parse(faceTop)) + "";

                    string box = "";
                    if (!"".Equals(x1) && !"".Equals(x2) && !"".Equals(y1) && !"".Equals(y2))
                    {
                        box = String.Format("{0},{1},{2},{3}", x1, y1, x2, y2);
                    }
                    Row.Add(NewColumn("Box_Coordinates", box, null));

                    x1 = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['left']").Single().Value.ToString();
                    y1 = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['top']").Single().Value.ToString();

                    string extWidth = "";
                    try
                    {
                        extWidth = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['width']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }

                    string extLeft = "";
                    try
                    {
                        extLeft = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['left']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string extHeight = "";
                    try
                    {
                        extHeight = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['height']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    string extTop = "";
                    try
                    {
                        extTop = context.SelectNodes(json, "$['faces'][" + i + "]['extended_coordinate']['top']").Single().Value.ToString();
                    }
                    catch (Exception)
                    {
                    }
                    extWidth = extWidth == null || "".Equals(extWidth.Trim()) ? "0" : extWidth;
                    extLeft = extLeft == null || "".Equals(extLeft.Trim()) ? "0" : extLeft;
                    extHeight = extHeight == null || "".Equals(extHeight.Trim()) ? "0" : extHeight;
                    extTop = extTop == null || "".Equals(extTop.Trim()) ? "0" : extTop;

                    x2 = (double.Parse(extWidth)
                        + double.Parse(extLeft)) + "";
                    y2 = (double.Parse(extHeight)
                        + double.Parse(extTop)) + "";

                    string pic = "";
                    if (!"".Equals(x1) && !"".Equals(x2) && !"".Equals(y1) && !"".Equals(y2))
                    {
                        pic = String.Format("{0},{1},{2},{3}", x1, y1, x2, y2);
                    }
                    Row.Add(NewColumn("Picture_Coordinates", pic, null));
                    #endregion
                    rows_body.Add(Row);
                }
            }
            if (rows_body != null)
            {
                ele_body.Add(rows_body);
                Elements.Add(ele_body);
            }


            Group.Add(Elements);
            Groups.Add(Group);
            root.Add(Groups);
            ////////////////////关键代码//////////////////////////////////
            string newPath = file.Directory.FullName + "\\" + fileName + ".xml";
            FileInfo newFileInfo = new FileInfo(newPath);
            if (newFileInfo.Exists)
            {
                newFileInfo.Delete();
            }
            else
            {
                //if (timer2.Enabled)
                //{
                //写入已经生成xml的json中
                Dao.DaoUtil.writeLog(file.FullName.Replace(rootPath, "") + "|" + file.CreationTime.Ticks, 3);
                //}
                root.Save(newPath);
            }
            Dao.DaoUtil.writeLog("读取完毕...", 0);
        }

        //get rect xy
        private static string GetRectXY(JObject json, JsonPathContext context, int i, string node)
        {
            string x = "";
            try
            {
                x = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates'][" + node + "]['x']").Single().Value.ToString();
            }
            catch (Exception)
            {
            }
            string y = "";
            try
            {
                y = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates'][" + node + "]['y']").Single().Value.ToString();
            }
            catch (Exception)
            {
            }
            string width = "";
            try
            {
                width = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates'][" + node + "]['width']").Single().Value.ToString();
            }
            catch (Exception)
            {
            }
            string height = "";
            try
            {
                height = context.SelectNodes(json, "$['faces'][" + i + "]['name_coordinates'][" + node + "]['height']").Single().Value.ToString();
            }
            catch (Exception)
            {
            }
            string coordinate = "";
            try
            {
                if (!"".Equals(x) && !"".Equals(y) && !"".Equals(width) && !"".Equals(height))
                {
                    coordinate = String.Format("{0},{1},{2},{3},0,2277,3201", x, y, double.Parse(width) + double.Parse(x), double.Parse(height) + double.Parse(y));
                }
            }
            catch (Exception)
            {
            }
            return coordinate;
        }

        //new column insert into rows
        private static XElement NewColumn(string name, string value, string xy)
        {
            XElement Column = new XElement("Column");

            Column.SetAttributeValue("name", name);
            XElement Datas = new XElement("Datas");

            XElement Data = new XElement("Data");
            Data.SetAttributeValue("status", "Key");
            Data.SetAttributeValue("id", "1");
            Data.SetValue(value);
            Datas.Add(Data);
            Column.Add(Datas);

            if (xy != null)
            {
                XElement Rect = new XElement("Rect");
                Rect.SetValue(xy);
                Column.Add(Rect);
            }
            return Column;
        }

        //监控文件变化
        public static void WatcherStart(string path, string filter)
        {

            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = path;
            watcher.Filter = filter;
            watcher.Created += new FileSystemEventHandler(OnProcess);
            watcher.Changed += new FileSystemEventHandler(OnProcess);
            watcher.EnableRaisingEvents = true;
            watcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess
                                   | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;
            watcher.IncludeSubdirectories = true;
        }

        private static void OnProcess(object source, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                //Console.WriteLine("文件新建了");
                OnCreated(source, e);
            }
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                //Console.WriteLine("文件更改了");
                OnCreated(source, e);
            }
        }

        //我们先来准备一个需要定时启动的方法
        static void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            while (processStatus)//如果状态锁被占用，需要等待五秒钟
            {
                Thread.Sleep(5 * 1000);
            }
            processStatus = true;//占有状态锁
            Console.WriteLine("扫描数据库");
            try
            {
                List<string> storageList = new List<string>(batchNames.ToArray());
                getBatchNameFromDB();
                var diffs = batchNames.Except(storageList).ToList();//差集
                if (diffs.Count() == 0)
                {
                    return;
                }
                else
                {

                    List<string> lines = new List<string>(File.ReadAllLines(nousedFile));//先读取到内存变量
                    for (int i = lines.Count - 1; i > 0; i--)
                    {
                        if (lines[i] != "")
                        {
                            handleFile(new FileInfo(rootPath + lines[i]));
                            lines.RemoveAt(i);
                            File.WriteAllLines(nousedFile, lines.ToArray());//在写回硬盤
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Dao.DaoUtil.writeLog("按时间生成xml异常:" + ex.Message, 1);
            }
            finally
            {
                processStatus = false;//释放状态锁
            }
        }

    }
}
