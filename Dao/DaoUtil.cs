using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using MySql.Data.MySqlClient;
using System.Data;
using System.Configuration;

namespace Dao
{
    class DaoUtil
    {
        #region  建立MySql数据库连接
        /// <summary>
        /// 建立数据库连接.
        /// </summary>
        /// <returns>返回MySqlConnection对象</returns>
        public static MySqlConnection getmysqlcon()
        {

            string M_str_sqlcon = ConfigurationManager.AppSettings["ConnectionString"].ToString();
            MySqlConnection myCon = new MySqlConnection(M_str_sqlcon);

            return myCon;
        }
        #endregion

        #region  执行MySqlCommand命令
        /// <summary>
        /// 执行MySqlCommand
        /// </summary>
        /// <param name="M_str_sqlstr">SQL语句</param>
        public static void getmysqlcom(string M_str_sqlstr)
        {
            MySqlConnection mysqlcon = getmysqlcon();
            mysqlcon.Open();
            MySqlCommand mysqlcom = new MySqlCommand(M_str_sqlstr, mysqlcon);
            mysqlcom.ExecuteNonQuery();
            mysqlcom.Dispose();
            mysqlcon.Close();
            mysqlcon.Dispose();
        }
        #endregion

        #region  创建MySqlDataReader对象
        /// <summary>
        /// 创建一个MySqlDataReader对象
        /// </summary>
        /// <param name="M_str_sqlstr">SQL语句</param>
        /// <returns>返回MySqlDataReader对象</returns>
        public static MySqlDataReader getmysqlread(string M_str_sqlstr)
        {
            MySqlConnection mysqlcon = getmysqlcon();
            MySqlCommand mysqlcom = new MySqlCommand(M_str_sqlstr, mysqlcon);
            try
            {
                mysqlcon.Open();
            }
            catch (Exception ex)
            {
                mysqlcon.Close();
                writeLog("数据库连接失败：" + ex.Message, 1);
            }
            MySqlDataReader mysqlread = mysqlcom.ExecuteReader(CommandBehavior.CloseConnection);
            return mysqlread;
        }
        #endregion

        static List<FileInfo> FileList = new List<FileInfo>();
        public static List<FileInfo> GetAllFiles(DirectoryInfo dir)
        {
            FileInfo[] allFile = dir.GetFiles("*.Completed");
            foreach (FileInfo fi in allFile)
            {
                FileList.Add(fi);
            }
            DirectoryInfo[] allDir = dir.GetDirectories();
            foreach (DirectoryInfo d in allDir)
            {
                GetAllFiles(d);
            }
            return FileList;
        }

        public static void writeLog(string log, int logType)
        {
            if (null != log && !"".Equals(log))
            {
                if (logType == 0 || logType == 1)
                {
                    log = DateTime.Now + " " + log;
                }

                string filePath = "";
                if (logType == 0)
                {
                    filePath = Environment.CurrentDirectory + "\\正常日志.txt";
                }
                else if (logType == 1)
                {
                    filePath = Environment.CurrentDirectory + "\\异常日志.txt";
                }
                else if (logType == 2)
                {
                    filePath = Environment.CurrentDirectory + "\\未生成xml的json.txt";
                }
                else if (logType==3)
                {
                    filePath = Environment.CurrentDirectory + "\\生成xml的json.txt";
                }


                StreamWriter sw = File.AppendText(filePath);

                sw.WriteLine(log);

                sw.Flush();

                sw.Close();

            }
        }
    }

}
