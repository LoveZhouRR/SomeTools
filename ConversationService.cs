using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DBC.Logging;
using DBC.Ors.Models;
using DBC.Ors.Models.Infrastructures.MemberShip;
using DBC.Ors.Models.Sales;
using DBC.Ors.Services;
using DBC.Utils.Clone;
using DBC.WeChat.Models;
using DBC.WeChat.Models.Conversation;
using DBC.WeChat.Models.Conversation.Msg;
using DBC.WeChat.Models.Infrastructures;
using DBC.WeChat.Models.Sales;
using DBC.WeChat.Services.Components.Picture;
using DBC.WeChat.Services.Components.XMLSerialization;
using DBC.WeChat.Services.Conversation.Models;
using DBC.WeChat.Services.Logging;
using Res = DBC.WeChat.Services.Properties.Resources;
using Newtonsoft.Json;
using Product = DBC.WeChat.Models.Sales.Product;
using ProductQuery = DBC.WeChat.Models.Sales.ProductQuery;
using ProductTagQuery = DBC.WeChat.Models.Sales.ProductTagQuery;

namespace DBC.WeChat.Services.Conversation.Components
{
    public class ConversationService : IConversationService
    {
        public ILogger Logger { get; set; }
        public IModelService ModelService { get; set; }
        public WechatMenu DefaultMenu { get; set; }
        public string ProductUrl { get; set; }
        public string Ftp { get; set; }
        public PictureSize CoverSize { get; set; }
        public PictureSize ItemSize { get; set; }
        public bool HasDialogs { get; set; }
        public IPictureService PictureService { get; set; }

        #region 接口方法
        public string GetResponse(string postStr, long ownerID)
        {
            string responsetext = "";
            //解析类型
            var request = SerializationHelper.DeSerialize(typeof(RequestMSG), postStr) as RequestMSG;
            //解析数据
            switch (request.MsgType)
            {
                case "text":
                    TextRequest msg = (TextRequest)SerializationHelper.DeSerialize(typeof(TextRequest), postStr);
                    if (HasDialogs)
                    {
                        ModelService.Create(new Dialogue()
                        {
                            FromUserName = msg.FromUserName,
                            Content = msg.Content,
                            ToUserName = msg.ToUserName,
                            OwnerID = ownerID
                        });
                    }
                    responsetext = GetProductResponse(msg, ownerID);
                    break;
                case "event":
                    RequestEvent requestEvent = (RequestEvent)SerializationHelper.DeSerialize(typeof(RequestEvent), postStr);
                    responsetext = GetEventResponse(requestEvent, ownerID, postStr);
                    break;
                default:
                    break;
            }
            return responsetext;
        }

        public bool CreateMenu(WeChatAccount account, string menuStr = "")
        {
            if (menuStr == "")
            {
                var store =
                    ModelService.SelectOrEmpty(new StoreQuery() { IDs = new long[] { account.OwnerID.Value } })
                        .FirstOrDefault();
                if (store == null)
                    return false;
                menuStr = MakeMenu(store.Code, account.AppID);
            }
            try
            {
                string RequestUrl = string.Format(Res.MenuCreateURL, GetToken(account));
                HttpWebRequest Request = WebRequest.Create(RequestUrl) as HttpWebRequest;
                byte[] bs = Encoding.UTF8.GetBytes(menuStr);
                Request.Method = "Post";
                Request.ContentType = "application/x-www-form-urlencoded";
                Request.ContentLength = bs.Length;
                using (Stream reqStream = Request.GetRequestStream())
                {
                    reqStream.Write(bs, 0, bs.Length);
                    reqStream.Close();
                }
                // Logger.SafeLog(RequestUrl, Level.Info);
                WebResponse Response = Request.GetResponse();
                using (Stream outstream = Response.GetResponseStream())
                {
                    byte[] data = new byte[Response.ContentLength];
                    outstream.Read(data, 0, (int)Response.ContentLength);
                    var obj = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                    //Logger.SafeLog(data, Level.Info);
                    return obj.errcode == "0";
                }

            }
            catch (Exception e)
            {
                Logger.SafeLog(e, Level.Error);
                throw e;
            }

        }

        public bool DeleteMenu(WeChatAccount account)
        {
            try
            {
                string RequestUrl = string.Format(Res.MenuDeleteURL, GetToken(account));
                HttpWebRequest Request = WebRequest.Create(RequestUrl) as HttpWebRequest;
                Request.Method = "Get";
                // Logger.SafeLog(RequestUrl, Level.Info);
                WebResponse Response = Request.GetResponse();
                using (Stream outstream = Response.GetResponseStream())
                {
                    byte[] data = new byte[Response.ContentLength];
                    outstream.Read(data, 0, (int)Response.ContentLength);
                    var obj = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                    //Logger.SafeLog(data, Level.Info);
                    return obj.errcode == "0";
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e, Level.Error);
                return false;
            }
        }

        public string RequestOpenID(string code, long ownerID)
        {
            try
            {
                WeChatAccount account =
                    ModelService.SelectOrEmpty(new WeChatAccountQuery() { OwnerID = ownerID }).FirstOrDefault();
                if (account == null)
                    return null;
                string RequestUrl = string.Format(Res.RequestOpenIDURL, account.AppID, account.AppSecret, code);
                HttpWebRequest Request = WebRequest.Create(RequestUrl) as HttpWebRequest;
                Request.Method = "GET";
                WebResponse Response = Request.GetResponse();
                using (Stream outstream = Response.GetResponseStream())
                {
                    byte[] data = new byte[Response.ContentLength];
                    outstream.Read(data, 0, (int)Response.ContentLength);
                    var obj = (OpenIDResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(OpenIDResponse));
                    if (obj.access_token == null)
                    {
                        var error = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                        Logger.SafeLog(Encoding.UTF8.GetString(data), Level.Error);
                        return null;
                    }
                    return obj.openid;
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e.Message, Level.Error);
                return null;
            }
        }

        public UserInfo RequeUserInfo(long ownerID, string openid)
        {
            var account = ModelService.SelectOrEmpty(new WeChatAccountQuery() { OwnerID = ownerID }).FirstOrDefault();
            if (account == null)
                return new UserInfo();
            var token = GetToken(account);
            string RequestUrl = string.Format(Res.UserInfoRequestURL, token, openid);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RequestUrl);
            request.Method = "GET";
            var response = request.GetResponse();
            using (Stream stream = response.GetResponseStream())
            {
                byte[] data = new byte[response.ContentLength];
                stream.Read(data, 0, (int)response.ContentLength);
                var userInfo = (UserInfo)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(UserInfo));
                if (userInfo.openid == null)
                {
                    var error = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                    Logger.SafeLog(Encoding.UTF8.GetString(data), Level.Error);
                    return new UserInfo();
                }
                return userInfo;
            }
        }

        public Scene RequestPermanentScene(long ownerID)
        {
            Scene scene = new Scene()
            {
                OwnerID = ownerID
            };
            var account = ModelService.SelectOrEmpty(new WeChatAccountQuery() { OwnerID = ownerID }).FirstOrDefault();
            if (account == null)
                return null;
            var accessToken = GetToken(account);
            var sceneCount = ModelService.SelectOrEmpty(new SceneCountQuery()
            {
                OwnerID = ownerID,
            }).FirstOrDefault();
            if (sceneCount == null)
            {
                sceneCount = new SceneCount()
                {
                    OwnerID = ownerID,
                    Count = 1,
                };
                ModelService.Create(sceneCount);
            }
            if (sceneCount.Count > 1000)
            {
                return null;
            }
            string requestUrl = string.Format(Res.PermanentSceneRequestURL, accessToken);
            var request = (HttpWebRequest)WebRequest.Create(requestUrl);
            request.Method = "POST";
            using (Stream reqStream = request.GetRequestStream())
            {
                var poststr = Res.PermanentSceneJson.Replace("__SceneID", sceneCount.Count.ToString());
                poststr = poststr.Replace('\'', '"');
                var bs = Encoding.UTF8.GetBytes(poststr);
                reqStream.Write(bs, 0, bs.Length);
                reqStream.Close();
            }
            using (var response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                var result = reader.ReadToEnd();
                var ticket = (TicketResponse)JsonConvert.DeserializeObject(result, typeof(TicketResponse));
                if (ticket.ticket == null)
                {
                    Logger.SafeLog(result, Level.Error);
                    return null;
                }
                scene.Ticket = ticket.ticket;
                scene.WechatSceneID = sceneCount.Count;
            }
            ModelService.Create(scene);
            GetScenePic(scene);
            return scene;
        }


        #endregion

        #region 私有方法
        public void GetScenePic(Scene scene)
        {
            try
            {
                var ticket = System.Web.HttpUtility.UrlEncode(scene.Ticket);
                var request = (HttpWebRequest)WebRequest.Create(string.Format(Res.ScenePicRequestURL, ticket));
                request.Method = "GET";
                using (var response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream memoryStream = new MemoryStream())
                using (var image = Image.FromStream(stream))
                {
                    var pic = new ScenePic(scene);
                    image.Save(memoryStream, ImageFormat.Jpeg);
                    pic.Content = memoryStream.ToArray();
                    PictureService.Create(pic);
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e.Message, Level.Error);
                throw;
            }
        }

        public string GetToken(WeChatAccount account)
        {
            string token = "";
            if (account == null || account.OwnerID == null)
                return "";
            var accessToken = ModelService.SelectOrEmpty(new AccessTokenQuery() { OwnerID = account.OwnerID.Value }).FirstOrDefault();
            if (accessToken == null)
            {
                var response = RequestToken(account);
                if (response != null)
                {
                    AccessToken newToken = new AccessToken()
                    {
                        OwnerID = account.OwnerID,
                        Token = response.access_token,
                        ExpiresIn = response.expires_in - 50,
                        LastGetAt = DateTime.Now,
                    };
                    ModelService.Create(newToken);
                    token = newToken.Token;
                }
            }
            else
            {
                if (accessToken.Token == null ||
                    (accessToken.LastGetAt == null || accessToken.LastGetAt.Value.AddSeconds(accessToken.ExpiresIn ?? 7150) < DateTime.Now))
                {
                    var response = RequestToken(account);
                    accessToken.Token = response.access_token;
                    accessToken.ExpiresIn = response.expires_in - 50;
                    accessToken.LastGetAt = DateTime.Now;
                    ModelService.Update(accessToken);
                }
                token = accessToken.Token;
            }
            return token;
        }

        private TokenResponse RequestToken(WeChatAccount account)
        {
            try
            {
                string RequestUrl = string.Format(Res.TokenRequestURL, "client_credential", account.AppID, account.AppSecret);
                HttpWebRequest Request = WebRequest.Create(RequestUrl) as HttpWebRequest;
                Request.Method = "GET";
                Logger.SafeLog(RequestUrl, Level.Info);
                using (WebResponse Response = Request.GetResponse())
                using (Stream outstream = Response.GetResponseStream())
                using (StreamReader reader = new StreamReader(outstream))
                {
                    var result = reader.ReadToEnd();
                    TokenResponse obj = (TokenResponse)JsonConvert.DeserializeObject(result, typeof(TokenResponse));
                    if (obj.access_token == null)
                    {
                        Logger.SafeLog(result, Level.Error);
                        return null;
                    }
                    return obj;
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e, Level.Error);
                return null;
            }
        }

        public string GetProductResponse(TextRequest msg, long ownerID)
        {
            string responsetext = "";
            var tags = ModelService.SelectOrEmpty(new TagQuery()
            {
                OwnerID = ownerID,
                NamePattern = msg.Content
            }).ToArray();
            var x = tags.Select(o => o.ID).OfType<long>().ToArray();
            if (!tags.Any())
            {
                return responsetext;
            }
            var productTags = ModelService.SelectOrEmpty(new ProductTagQuery()
            {
                TagIDs = tags.Select(o => o.ID).OfType<long>().ToArray(),
            });
            var products = ModelService.SelectOrEmpty(new ProductQuery()
            {
                IDs = productTags.Select(o => o.ProductID).OfType<long>().ToArray(),
                Includes = new string[] { "ProductPictures" },
                Shelved = true,
                Take = 5,
                OrderField = "LastModifiedAt",
                OrderDirection = OrderDirection.Desc,
            }).ToArray();
            responsetext = MakeProductResponse(products, msg.FromUserName, msg.ToUserName);
            return responsetext;
        }

        private string MakeProductResponse(Product[] products, string toUser, string fromUser)
        {
            string responsetext = "";
            if (!products.Any()) return responsetext;
            var ownerID = products.FirstOrDefault().OwnerID.Value;
            var account = ModelService.SelectOrEmpty(new WeChatAccountQuery()
            {
                OwnerID = ownerID,
            }).FirstOrDefault();
            if (account == null)
                return responsetext;
            var store = ModelService.SelectOrEmpty(new StoreQuery() { IDs = new long[] { ownerID } }).FirstOrDefault();
            if (store == null) return responsetext;
            var news = new NewsResponse()
            {
                FromUserName = fromUser,
                ToUserName = toUser,
                MsgType = "news",
                CreateTime = ConvertDateTimeInt(DateTime.Now),
            };
            var articles = new List<ArticleItem>();
            for (int i = 0; i < products.Count(); i++)
            {
                var item = products[i];
                string picUrl = "";
                var pic = item.ProductPictures.FirstOrDefault(o => o.IsFirst.Value);
                PictureSize size = ItemSize;
                if (i == 0)
                    size = CoverSize;
                if (pic != null)
                {
                    picUrl = Path.Combine(Ftp, pic.Path, GetName(pic, size));
                    picUrl = picUrl.Replace('\\', '/');
                }
                var article = new ArticleItem()
                {
                    Title = item.Name,
                    Description = item.Description,
                    Url = string.Format(Res.RedirectURL, account.AppID, string.Format(ProductUrl, store.Code, item.ID)),
                    PicUrl = picUrl,
                };
                articles.Add(article);
            }
            news.Articles = articles;
            news.ArticleCount = articles.Count();
            responsetext = SerializationHelper.Serialize(news);
            return responsetext;
        }

        public string GetKeyResponse(TextRequest msg, long ownerID)
        {
            string responsetext = "";
            //检索关键字
            var key = ModelService.Select(new KeyWordQuery() { Content = msg.Content, OwnerID = ownerID }).FirstOrDefault();
            long? ruleID = 0;
            if (key == null || key.KeyWordGroupID == null)
            {
                var rule = ModelService.Select(new KeyWordGroupQuery() { OwnerID = ownerID, Type = (int)RuleType.Default }).FirstOrDefault();
                if (rule != null)
                    ruleID = rule.ID;
            }
            else
            {
                ruleID = key.KeyWordGroupID;
            }
            //随即回复
            var reply = ModelService.Select(new ReplyQuery() { KeyWordGroupID = ruleID }).FirstOrDefault();
            if (reply != null)
            {
                switch (reply.Type)
                {
                    case (int)ReplyType.news:
                        break;
                    case (int)ReplyType.text:
                        var textReplys =
                            ModelService.Select(new TextReplyItemQuery() { OwnerID = ownerID, ParentID = reply.ID }).ToArray();
                        Random random = new Random();
                        var index = random.Next(0, textReplys.Count() - 1);
                        var textReply = textReplys[index];
                        responsetext = MakeTextResponse(textReply, msg.FromUserName, msg.ToUserName);
                        break;
                }
            }
            return responsetext;
        }

        private string GetEventResponse(RequestEvent msg, long ownerID, string postStr)
        {
            string response = "";
            switch (msg.Event.ToLower())
            {
                //订阅事件
                case "subscribe":
                    var orginFan = ModelService.SelectOrEmpty(new FanQuery() { Code = msg.FromUserName, OwnerID = ownerID }).FirstOrDefault();
                    var userInfo = RequeUserInfo(ownerID, msg.FromUserName);
                    if (orginFan != null)
                    {
                        orginFan.Status = (int)FanStatus.Subscribe;
                        orginFan.Name = userInfo.nickname;
                        ModelService.Update(orginFan);
                    }
                    else
                    {
                        Fan fan = new Fan()
                        {
                            Name = userInfo.nickname,
                            OwnerID = ownerID,
                            Code = msg.FromUserName,
                            Status = (int)FanStatus.Subscribe,
                        };
                        ModelService.Create(fan);
                    }
                    var scene = (SceneEvent)SerializationHelper.DeSerialize(typeof(SceneEvent), postStr);
                    //场景二维码事件
                    if (scene.Ticket != null)
                    {
                        response = GetSceneResponse(scene, ownerID);
                    }
                    else
                    {
                        response = GetSubscribeOrNotResponse(ownerID, msg.FromUserName, msg.ToUserName, true);
                    }
                    break;
                case "unsubscribe":
                    var foucusFan =
                        ModelService.SelectOrEmpty(new FanQuery() { OwnerID = ownerID, Code = msg.FromUserName }).FirstOrDefault();
                    if (foucusFan != null)
                    {
                        foucusFan.Status = (int)FanStatus.UnSubscribe;
                        ModelService.Update(foucusFan);
                    }
                    response = GetSubscribeOrNotResponse(ownerID, msg.FromUserName, msg.ToUserName, false);
                    break;
                case "click":
                    var clickevent = (MenuEvent)SerializationHelper.DeSerialize(typeof(MenuEvent), postStr);
                    response = GetClickResponse(clickevent, ownerID);
                    break;
                case "scan":
                    var scanScene = (SceneEvent)SerializationHelper.DeSerialize(typeof(SceneEvent), postStr);
                    response = GetSceneResponse(scanScene, ownerID);
                    break;
            }
            return response;
        }

        private string GetClickResponse(MenuEvent menuEvent,long ownerid)
        {
            var response = "";
            switch (menuEvent.EventKey)
            {
                case "NewProduct":
                    var text = new TextRequest()
                    {
                        Content = "新品",
                        FromUserName = menuEvent.FromUserName,
                        ToUserName = menuEvent.ToUserName,
                    };
                    response=GetProductResponse(text, ownerid);
                    if (response == "")
                    {
                        var defaultResponse = new TextReplyItem()
                        {
                            Content = "暂时没有新品上架",
                        };
                        response = MakeTextResponse(defaultResponse, menuEvent.FromUserName, menuEvent.ToUserName);
                    }
                    break;
            }
            return response;
        }

        private string GetSceneResponse(SceneEvent scene, long ownerID)
        {
            string response = "";
            scene.EventKey = scene.EventKey.Replace(scene.Event == "subscribe" ? "qrscene_" : "SCENE_", string.Empty);
            var localscene = ModelService.Select(new SceneQuery()
            {
                OwnerID = ownerID,
                WechatSceneID = Convert.ToInt32(scene.EventKey),
            });
            var productScene = ModelService.Select(new ProductSceneQuery()
            {
                OwnerID = ownerID,
                SceneID = localscene.FirstOrDefault().ID,
            });
            if (!productScene.Any())
                return response;
            var products = ModelService.Select(new ProductQuery()
            {
                OwnerID = ownerID,
                IDs = productScene.Select(o => o.ProductID).OfType<long>().ToArray(),
                Includes = new []{"ProductPictures"}
            });
            response = MakeProductResponse(products.ToArray(), scene.FromUserName, scene.ToUserName);
            return response;
        }

        private string GetMenuClickResponse(MenuEvent menuEvent)
        {
            string response = "";
            return response;
        }

        private string GetSubscribeOrNotResponse(long ownerID, string toUser, string fromUser, bool subscribe)
        {
            string response = "";
            int type = subscribe ? (int)RuleType.Subscribe : (int)RuleType.UnSubscribe;
            var rule = ModelService.SelectOrEmpty(new KeyWordGroupQuery() { OwnerID = ownerID, Type = type }).FirstOrDefault();
            if (rule == null)
                return response;
            var reply = ModelService.SelectOrEmpty(new ReplyQuery() { OwnerID = ownerID, KeyWordGroupID = rule.ID }).FirstOrDefault();
            if (reply == null)
                return response;
            switch (reply.Type)
            {
                case (int)ReplyType.text:
                    var textreply = ModelService.SelectOrEmpty(new TextReplyItemQuery() { ParentID = reply.ID, OwnerID = ownerID }).FirstOrDefault();
                    if (textreply != null)
                        response = MakeTextResponse(textreply, toUser, fromUser);
                    break;
                case (int)ReplyType.news:
                    break;
            }
            return response;
        }

        private string MakeTextResponse(TextReplyItem text, string toUser, string fromUser)
        {
            TextResponse response = new TextResponse
            {
                FromUserName = fromUser,
                ToUserName = toUser,
                CreateTime = ConvertDateTimeInt(DateTime.Now),
                MsgType = "text",
                Content = text.Content
            };
            return SerializationHelper.Serialize(response);
        }

        private int ConvertDateTimeInt(System.DateTime time)
        {
            System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1));
            return (int)(time - startTime).TotalSeconds;
        }

        private string GetName(IPicture pic, PictureSize size)
        {
            return size.Width + "_" + size.Height + "_" + pic.Name;
        }

        private string MakeMenu(string storcode, string appID)
        {
            var menu = new WechatMenu();
            List<WechatParent> buttonList = new List<WechatParent>();
            foreach (var button in DefaultMenu.button)
            {
                var parent = new WechatParent()
                {
                    name = button.name
                };
                var sublist = new List<WechatAllButton>();
                foreach (var sub in button.sub_button)
                {
                    var submenu = new WechatAllButton()
                    {
                        name = sub.name,
                        type = sub.type,
                        key = sub.key,
                    };
                    if (!string.IsNullOrEmpty(sub.url))
                    {
                        submenu.url = string.Format(Res.RedirectURL, appID, string.Format(sub.url, storcode));
                    }
                    else
                    {
                        submenu.url = "#";
                    }
                    sublist.Add(submenu);
                }
                parent.sub_button = sublist.ToArray();
                buttonList.Add(parent);
            }
            menu.button = buttonList.ToArray();
            return JsonConvert.SerializeObject(menu);
        }

        public Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
        #endregion

    }
}
