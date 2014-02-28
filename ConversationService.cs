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
using DBC.WeChat.Services.Components.Files;
using DBC.WeChat.Services.Components.Picture;
using DBC.WeChat.Services.Components.XMLSerialization;
using DBC.WeChat.Services.Conversation.Models;
using DBC.WeChat.Services.Logging;
using Res = DBC.WeChat.Services.Properties.Resources;
using Newtonsoft.Json;
using Picture = DBC.WeChat.Models.Conversation.Msg.Picture;
using Product = DBC.WeChat.Models.Sales.Product;
using ProductQuery = DBC.WeChat.Models.Sales.ProductQuery;
using ProductTagQuery = DBC.WeChat.Models.Sales.ProductTagQuery;

namespace DBC.WeChat.Services.Conversation.Components
{
    public class ConversationService : IConversationService
    {
        /// <summary>
        /// 日志
        /// </summary>
        public ILogger Logger { get; set; }
        public IModelService ModelService { get; set; }
        /// <summary>
        /// 创建标准化的自定义菜单
        /// </summary>
        public WechatMenu DefaultMenu { get; set; }
        /// <summary>
        /// 关键词商品回复需要
        /// </summary>
        public string ProductUrl { get; set; }
        /// <summary>
        /// 关键词商品回复需要
        /// </summary>
        public string Ftp { get; set; }
        public PictureSize CoverSize { get; set; }
        public PictureSize ItemSize { get; set; }
        /// <summary>
        /// 是否记录用户对话信息
        /// </summary>
        public bool HasDialogs { get; set; }
        /// <summary>
        /// 生成场景二维码时需要
        /// </summary>
        public IPictureService PictureService { get; set; }
        /// <summary>
        /// 该属性在媒体上传接口中需要
        /// </summary>
        public HttpUpload UploadCompenent { get; set; }

        #region 接口方法
        /// <summary>
        /// 消息回复
        /// </summary>
        /// <param name="postStr">微信消息字符串包含用户信息</param>
        /// <param name="ownerID"></param>
        /// <returns></returns>
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
                    if (!CheckReSendMsg(msg, ownerID))
                        break;
                    if (HasDialogs)
                    {
                        ModelService.Create(new Dialogue()
                        {
                            FromUserName = msg.FromUserName,
                            Content = msg.Content,
                            ToUserName = msg.ToUserName,
                            OwnerID = ownerID,
                            CheckSign = msg.FromUserName+msg.CreateTime,
                        });
                    }
                    SendKeyResponse(msg, ownerID);
                    responsetext = GetProductResponse(msg, ownerID);
                    break;
                case "event":
                    RequestEvent requestEvent = (RequestEvent)SerializationHelper.DeSerialize(typeof(RequestEvent), postStr);
                    if (!CheckReSendMsg(requestEvent, ownerID))
                        break;
                    if (HasDialogs)
                    {
                        ModelService.Create(new Dialogue()
                        {
                            FromUserName = requestEvent.FromUserName,
                            ToUserName = requestEvent.ToUserName,
                            OwnerID = ownerID,
                            CheckSign = requestEvent.FromUserName + requestEvent.CreateTime,
                        });
                    }
                    responsetext = GetEventResponse(requestEvent, ownerID, postStr);
                    break;
                default:
                    break;
            }
            return responsetext;
        }

        /// <summary>
        /// 创建标准自定义菜单
        /// </summary>
        /// <param name="account"></param>
        /// <param name="menuStr">默认为DefaultMenu,可以直接输入json字符串</param>
        /// <returns></returns>
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
                //坑爹的一次演示
                if (account.AppID == "wxb6dcbbbc51b694cb")
                {
                    
                    menuStr = @"{
                     'button':[
                        {
                        'name':'关于我们',
                        'type':'view',
                        'url':'https://open.weixin.qq.com/connect/oauth2/authorize?appid=wx164a5bf915aa6725&redirect_uri=http://dbcec.com/Mall/7EU9/home/companyintro&response_type=code&scope=snsapi_base&state=1&from=message&isappinstalled=0#wechat_redirect'
                        },
                      {
                           'name':'互动活动',
                           'sub_button':[
                           {	
                               'type':'view',
                               'name':'刮刮卡',
                               'url':'https://open.weixin.qq.com/connect/oauth2/authorize?appid=wxb6dcbbbc51b694cb&redirect_uri=http://dbcec.com/Mall/7EU9/Activity/ScratchCard&response_type=code&scope=snsapi_base&state=1&from=message&isappinstalled=0#wechat_redirect'
                            }
                            ]
                       },
                       {
                           'name':'服务中心',
                           'sub_button':[
                           {	
                               'type':'view',
                               'name':'中奖历史',
                               'url':'https://open.weixin.qq.com/connect/oauth2/authorize?appid=wxb6dcbbbc51b694cb&redirect_uri=http://dbcec.com/Mall/7EU9/Activity/AwardHistory&response_type=code&scope=snsapi_base&state=1&from=message&isappinstalled=0#wechat_redirect'
                            }
                            ]
                       }
                       ]
                 }";
                    menuStr = menuStr.Replace("'", "\"");
                }
            }
            try
            {
                this.Logger.SafeLog(menuStr, Level.Info);
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
                Logger.SafeLog(RequestUrl, Level.Info);
                WebResponse Response = Request.GetResponse();
                using (Stream outstream = Response.GetResponseStream())
                {
                    byte[] data = new byte[Response.ContentLength];
                    outstream.Read(data, 0, (int)Response.ContentLength);
                    var obj = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                    Logger.SafeLog(Encoding.UTF8.GetString(data), Level.Info);
                    return obj.Errcode == "0";
                }

            }
            catch (Exception e)
            {
                Logger.SafeLog(e, Level.Error);
                throw e;
            }

        }

        /// <summary>
        /// 删除自定义菜单
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
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
                    return obj.Errcode == "0";
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e, Level.Error);
                return false;
            }
        }


        /// <summary>
        /// 请求用户openid
        /// </summary>
        /// <param name="code">微信授权验证接口返回的参数，详情查询微信接口说明</param>
        /// <param name="ownerID"></param>
        /// <returns></returns>
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
                    if (obj.AccessToken == null)
                    {
                        var error = (EventResponse)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), typeof(EventResponse));
                        Logger.SafeLog(Encoding.UTF8.GetString(data), Level.Error);
                        return null;
                    }
                    return obj.Openid;
                }
            }
            catch (Exception e)
            {
                Logger.SafeLog(e.Message, Level.Error);
                return null;
            }
        }

        /// <summary>
        /// 请求用户个人信息
        /// </summary>
        /// <param name="ownerID"></param>
        /// <param name="openid"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 请求场景二维码
        /// </summary>
        /// <param name="ownerID"></param>
        /// <returns></returns>
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
                if (ticket.Ticket == null)
                {
                    Logger.SafeLog(result, Level.Error);
                    return null;
                }
                scene.Ticket = ticket.Ticket;
                scene.WechatSceneID = sceneCount.Count;
            }
            ModelService.Create(scene);
            GetScenePic(scene);
            return scene;
        }

        /// <summary>
        /// 多媒体上传
        /// </summary>
        /// <param name="account"></param>
        /// <param name="resource"></param>
        /// <returns></returns>
        public string UploadMedia(WeChatAccount account, FileResource resource)
        {
            string type = "";
            switch (resource.Type)
            {
                case ResourceType.Picture:
                    type = "image";
                    break;
                case ResourceType.Audio:
                    type = "voice";
                    break;
                case ResourceType.Video:
                    type = "video";
                    break;
            }
            string token = GetToken(account);
            string url = string.Format(Res.UploadMediaURL, token, type);
            var response = UploadCompenent.PostMultipleFiles(url, new[] { System.IO.Path.Combine(resource.Path, resource.Name) });
            var obj = (MediaResponse)JsonConvert.DeserializeObject(response, typeof(MediaResponse));
            if (obj.MediaId != null)
            {
                return obj.MediaId;
            }
            else
            {
                var error = (EventResponse)JsonConvert.DeserializeObject(response, typeof(EventResponse));
                Logger.SafeLog(error, Level.Error);
                return "";
            }
        }


        #endregion

        #region 私有方法

        /// <summary>
        /// 无重复返回true
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="ownerID"></param>
        /// <returns></returns>
        public bool CheckReSendMsg(BaseWeChatMSG msg, long ownerID)
        {
            var dialog = ModelService.Select(new DialogueQuery()
            {
                CheckSign = msg.FromUserName + msg.CreateTime,
                OwnerID = ownerID,
            }).FirstOrDefault();
            return dialog == null;
        }

        public bool CheckReSendMsg(MediaResponse msg, long ownerID)
        {
            var dialog = ModelService.Select(new DialogueQuery()
            {
                CheckSign = msg.MediaId,
                OwnerID = ownerID,
            }).FirstOrDefault();
            return dialog == null;
        }


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
                        Token = response.AccessToken,
                        ExpiresIn = response.ExpiresIn - 50,
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
                    accessToken.Token = response.AccessToken;
                    accessToken.ExpiresIn = response.ExpiresIn - 50;
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
                    if (obj.AccessToken == null)
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

        public void SendKeyResponse(TextRequest msg, long ownerID)
        {
            //检索关键字
            var key = ModelService.Select(new KeyWordQuery() { Content = msg.Content, OwnerID = ownerID }).FirstOrDefault();
            var rulequery = new RuleQuery() { OwnerID = ownerID };
            if (key == null || key.RuleID == null)
            {
                rulequery.Type = (int)RuleType.Default;
            }
            else
            {
                rulequery.IDs = new long[] { key.RuleID.Value };
            }

            var rule = ModelService.SelectOrEmpty(rulequery).FirstOrDefault();
            if (rule == null)
                return;
            var replyquery = new ReplyQuery() { RuleID = rule.ID };
            var reply = ModelService.Select(replyquery).FirstOrDefault();
            if (reply == null)
                return;
            var account = ModelService.Select(new WeChatAccountQuery() { OwnerID = ownerID }).FirstOrDefault();
            string token = GetToken(account);
            //全部回复
            if (reply.ReplyAll != null && reply.ReplyAll.Value)
            {
                replyquery.Includes = new[] { "TextReplyItems", "ResourceItems" };
                reply = ModelService.Select(replyquery).FirstOrDefault();

                foreach (var item in reply.TextReplyItems)
                {
                    var textMsg = MakeJosnMsg(item, msg.FromUserName);
                    CustomerServiceResponse(token, textMsg);
                }
                foreach (var item in reply.ResourceItems)
                {
                    var resourceMsg = MakeJosnMsg(item, msg.FromUserName);
                    CustomerServiceResponse(token, resourceMsg);
                }
            }
            //随机回复
            else
            {
                CustomerServiceResponse(token, MakeRandomResponse(reply, msg.FromUserName));
            }
        }


        private ResponseMSG MakeRandomResponse(Reply reply, string toUser)
        {
            var textquery = new TextReplyItemQuery()
            {
                OwnerID = reply.OwnerID,
                ParentID = reply.ID,
                Take = 0,
                Skip = 0,
            };
            ModelService.Select(textquery);
            var textreplycount = textquery.Count ?? 0;
            var resourcequery = new ReplyResourceItemQuery()
            {
                OwnerID = reply.OwnerID,
                ReplyID = reply.ID,
                Take = 0,
                Skip = 0,
            };
            var resourcecount = resourcequery.Count ?? 0;
            var random = new Random();
            var index = random.Next(0, (int)(textreplycount + resourcecount+1));
            if (index < textreplycount)
            {
                textquery.Take = 1;
                textquery.Skip = index;
                var textreply = ModelService.Select(textquery).FirstOrDefault();
                return MakeJosnMsg(textreply, toUser);
            }
            else
            {
                resourcequery.Take = 1;
                resourcequery.Skip = index - textreplycount - 1;
                var resourcereply = ModelService.Select(resourcequery).FirstOrDefault();
                return MakeJosnMsg(resourcereply, toUser);
            }
        }

        private ResponseMSG MakeJosnMsg(TextReplyItem item, string toUser)
        {
            var response = new JsonTextResponse()
            {
                ToUserName = toUser,
                MsgType = "text",
                Text = new Text()
                {
                    Content = item.Content
                }
            };
            return response;

        }

        private ResponseMSG MakeJosnMsg(ReplyResourceItem item, string toUser)
        {
            switch (item.ResourceType)
            {
                case (int)ResourceType.Picture:
                    var pic = ModelService.Select(new PictureResourceQuery()
                    {
                        OwnerID = item.OwnerID,
                        IDs = new long[] { item.ResourceID.Value },
                    }).FirstOrDefault();
                    return new JsonImageResponse()
                    {
                        ToUserName = toUser,
                        MsgType = "image",
                        Picture = new Picture() { MediaID = pic.MediaId }
                    };
                case (int)ResourceType.Audio:
                    var audio = ModelService.Select(new AudioResourceQuery()
                   {
                       OwnerID = item.OwnerID,
                       IDs = new long[] { item.ResourceID.Value },
                   }).FirstOrDefault();
                    return new JsonVoiceResponse()
                    {
                        ToUserName = toUser,
                        MsgType = "voice",
                        Voice = new Voice() { MediaID = audio.MediaId }
                    };
                case (int)ResourceType.Video:
                    var video = ModelService.Select(new VideoResourceQuery()
                   {
                       OwnerID = item.OwnerID,
                       IDs = new long[] { item.ResourceID.Value },
                   }).FirstOrDefault();
                    return new JsonVideoResponse()
                    {
                        ToUserName = toUser,
                        MsgType = "video",
                        Video = new Video()
                        {
                            MediaID = video.MediaId,
                            Description = video.Description,
                            Title = video.Title
                        }
                    };
                case (int)ResourceType.News:
                    var cover = ModelService.Select(new NewsResourceQuery()
                    {
                        OwnerID = item.OwnerID,
                        IDs = new long[] {item.ResourceID.Value}
                    }).FirstOrDefault();
                    var articles = new List<Article>();
                    articles.Add(new Article()
                    {
                        Description = cover.Description,
                        PicUrl = cover.PicUrl,
                        Title = cover.Title,
                        Url = cover.Title,
                    });
                    if (!cover.Single.Value)
                    {
                        var items = ModelService.Select(new NewsResourceQuery()
                        {
                            OwnerID = item.OwnerID,
                            ParentID = item.ResourceID,
                        });
                        articles.AddRange(items.Select(o => new Article()
                        {
                            Description = o.Description,
                            PicUrl = o.PicUrl,
                            Title = o.Title,
                            Url = o.Title,
                        }));
                    }
                    return new JsonNewsResponse()
                    {
                        ToUserName = toUser,
                        MsgType = "news",
                        News = new News()
                        {
                            Articles = articles
                        }
                    };
                default:
                    return new ResponseMSG();
            }
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

        private string GetClickResponse(MenuEvent menuEvent, long ownerid)
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
                    response = GetProductResponse(text, ownerid);
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
                Includes = new[] { "ProductPictures" }
            });
            response = MakeProductResponse(products.ToArray(), scene.FromUserName, scene.ToUserName);
            return response;
        }

        private string GetMenuClickResponse(MenuEvent menuEvent)
        {
            string response = "";
            return response;
        }

        /// <summary>
        /// 发送客服消息
        /// </summary>
        /// <param name="token"></param>
        /// <param name="response"></param>
        public void CustomerServiceResponse(string token, ResponseMSG response)
        {
            var uri = new Uri(string.Format(Res.CustomerServiceURL, token));
            var data = WebClientPostJson(uri, JsonConvert.SerializeObject(response));
            Logger.SafeLog(data,Level.Info);
            var obj = (EventResponse)JsonConvert.DeserializeObject(data, typeof(EventResponse));
            if (obj.Errcode != "0")
            {
                string error = string.Format("Touser:{0}---errorcode:{1}---errormsg:{2}", response.ToUserName, obj.Errcode, obj.Errmsg);
                Logger.SafeLog(error, Level.Error);
            }
        }

        private string WebClientPostJson(Uri uri, string data)
        {
            WebClient client = new WebClient();
            client.Encoding = System.Text.Encoding.UTF8;
            client.Headers.Add(HttpRequestHeader.Accept, "json");
            client.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded; charset=UTF-8");
            return client.UploadString(uri, "POST", data);
        }

        private string GetSubscribeOrNotResponse(long ownerID, string toUser, string fromUser, bool subscribe)
        {
            string response = "";
            int type = subscribe ? (int)RuleType.Subscribe : (int)RuleType.UnSubscribe;
            var rule = ModelService.SelectOrEmpty(new RuleQuery() { OwnerID = ownerID, Type = type }).FirstOrDefault();
            if (rule == null)
                return response;
            var reply = ModelService.SelectOrEmpty(new ReplyQuery() { OwnerID = ownerID, RuleID = rule.ID }).FirstOrDefault();
            if (reply == null)
                return response;
            switch (reply.Type)
            {
                case (int)ReplyType.Text:
                    var textreply = ModelService.SelectOrEmpty(new TextReplyItemQuery() { ParentID = reply.ID, OwnerID = ownerID }).FirstOrDefault();
                    if (textreply != null)
                        response = MakeTextResponse(textreply, toUser, fromUser);
                    break;
                case (int)ReplyType.News:
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
