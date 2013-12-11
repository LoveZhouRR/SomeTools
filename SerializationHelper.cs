using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DBC.WeChat.Services.Components.XMLSerialization
{
    public class SerializationHelper
    {
        private SerializationHelper()
        { }

        private static Dictionary<int, XmlSerializer> serializer_dict = new Dictionary<int, XmlSerializer>();

        public static XmlSerializer GetSerializer(Type t)
        {
            int type_hash = t.GetHashCode();

            if (!serializer_dict.ContainsKey(type_hash))
            {
                serializer_dict.Add(type_hash, new XmlSerializer(t));
            }
            return serializer_dict[type_hash];
        }

        /// <summary>
        /// 反序列化
        /// </summary>
        /// <param name="type">对象类型</param>
        /// <param name="filename">文件路径</param>
        /// <returns></returns>
        public static object Load(Type type, string filename)
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                XmlSerializer serializer = new XmlSerializer(type);
                return serializer.Deserialize(fs);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                }
            }
        }

        /// <summary>
        /// xml序列化成字符串
        /// </summary>
        /// <param name="obj">对象</param>
        /// <returns>xml字符串</returns>
        public static string Serialize(object obj)
        {
            string returnStr = "";
            XmlSerializer serializer = GetSerializer(obj.GetType());
            MemoryStream ms = new MemoryStream();
            XmlWriter xtw = null;
            StreamReader sr = null;
            try
            {
                var ns = new XmlSerializerNamespaces();
                ns.Add("", "");
                var setting = new XmlWriterSettings()
                    {
                        OmitXmlDeclaration = true,
                        Encoding = Encoding.UTF8,
                        Indent = true
                    };
                xtw = System.Xml.XmlWriter.Create(ms, setting);
                //xtw.Formatting = System.Xml.Formatting.Indented;
                serializer.Serialize(xtw, obj, ns);
                ms.Seek(0, SeekOrigin.Begin);
                sr = new StreamReader(ms);
                returnStr = sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if (xtw != null)
                    xtw.Close();
                if (sr != null)
                    sr.Close();
                ms.Close();
            }
            return returnStr;
        }

        public static object DeSerialize(Type type, string s)
        {
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s);
            try
            {
                XmlSerializer serializer = GetSerializer(type);
                return serializer.Deserialize(new MemoryStream(b));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static string SerializeBySimplify(object obj)
        {
            string returnStr = "";
            XmlSerializer serializer = GetSerializer(obj.GetType());
            MemoryStream ms = new MemoryStream();
            XmlTextWriter xtw = null;
            StreamReader sr = null;
            try
            {
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces(); //Add an empty namespace and empty value
                ns.Add("", "");

                //XmlWriterSettings settings = new XmlWriterSettings(); // Remove the <?xml version="1.0" encoding="utf-8"?>
                //settings.OmitXmlDeclaration = true;

                xtw = new System.Xml.XmlTextWriter(ms, Encoding.UTF8);
                xtw.Formatting = System.Xml.Formatting.None;

                serializer.Serialize(xtw, obj, ns);
                ms.Seek(0, SeekOrigin.Begin);
                sr = new StreamReader(ms);
                returnStr = sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if (xtw != null)
                {
                    xtw.Close();
                }
                if (sr != null)
                {
                    sr.Close();
                }
                ms.Close();
            }
            return returnStr;
        }
    }

}