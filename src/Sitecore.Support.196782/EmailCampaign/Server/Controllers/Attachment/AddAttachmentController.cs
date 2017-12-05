using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Http;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.EmailCampaign.Server.Contexts;
using Sitecore.Support.EmailCampaign.Server.Filters;
using Sitecore.EmailCampaign.Server.Responses;
using Sitecore.ExM.Framework.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Modules.EmailCampaign;
using Sitecore.Modules.EmailCampaign.Core;
using Sitecore.Modules.EmailCampaign.Messages;
using Sitecore.Resources.Media;
using Sitecore.Services.Core;
using Sitecore.Services.Infrastructure.Web.Http;

namespace Sitecore.Support.EmailCampaign.Server.Controllers.Attachment
{
    /// <summary>
    /// Defines the <see cref="AddAttachmentController"/> class.
    /// </summary>
    [ServicesController("EXM.AddAttachment")]
    [SitecoreAuthorize(Modules.EmailCampaign.Core.Constants.AdvancedUsersRoleName, Modules.EmailCampaign.Core.Constants.UsersRoleName)]
    public class AddAttachmentController : ServicesApiController
    {
        /// <summary>
        /// The factory.
        /// </summary>
        private readonly Factory _factory;

        /// <summary>
        /// The item utilities.
        /// </summary>
        private readonly ItemUtilExt _itemUtil;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddAttachmentController"/> class.
        /// </summary>
        public AddAttachmentController()
            : this(Factory.Instance, new ItemUtilExt(), Logger.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AddAttachmentController"/> class.
        /// </summary>
        /// <param name="factory"> The factory. </param>
        /// <param name="itemUtil"> The item utilities. </param>
        /// <param name="logger"> The logger. </param>
        public AddAttachmentController([NotNull] Factory factory, [NotNull] ItemUtilExt itemUtil, [NotNull] ILogger logger)
        {
            Assert.ArgumentNotNull(factory, "factory");
            Assert.ArgumentNotNull(itemUtil, "itemUtil");
            Assert.ArgumentNotNull(logger, "logger");

            _factory = factory;
            _itemUtil = itemUtil;
            _logger = logger;
        }

        /// <summary>
        /// The process.
        /// </summary>
        /// <param name="data">The request args.</param>
        /// <returns>The <see cref="Response"/>.</returns>
        [ActionName("DefaultAction")]
        public Response AddAttachment(AttachmentContext data)
        {
            Assert.ArgumentNotNull(data, "data");

            Assert.IsNotNull(data, "Could not get the attachment context for requestArgs:{0}", data);
            Assert.IsNotNullOrEmpty(data.MessageId, "Could not get message id from the attachmentContext for requestArgs:{0}", data);
            Assert.IsNotNullOrEmpty(data.AttachmentId, "Could not get file id from the attachmentContext for requestArgs:{0}", data);
            Assert.IsNotNullOrEmpty(data.FileName, "Could not get file name from the attachmentContext for requestArgs:{0}", data);

            var response = new AddAttachmentResponse
            {
                Error = true,
                ErrorMessage = string.Empty
            };
            try
            {
                var messageItem = _factory.GetMessageItem(data.MessageId, data.Language);
                if (messageItem == null)
                {
                    response.ErrorMessage = EcmTexts.Localize(EcmTexts.EditedMessageCouldNotBeFoundItMayHaveBeenMovedOrDeletedByAnotherUser);
                    return response;
                }

                if (messageItem.State != MessageState.Draft && messageItem.State != MessageState.Inactive)
                {
                    response.ErrorMessage = EcmTexts.Localize(EcmTexts.TheFileXCouldNotBeAttachedAnotherUserMayHaveChangedTheMessageStateFromDraftOrInactive, data.FileName);
                    return response;
                }

                var attachment = _itemUtil.GetItem(data.AttachmentId, Globalization.Language.Parse(data.Language), false);
                if (attachment == null)
                {
                    response.ErrorMessage = EcmTexts.Localize(EcmTexts.TheFileXHasNotBeenAttached, data.FileName);
                    return response;
                }

                var messageAttachments = messageItem.Source.Attachments;
                var mediaItem = new MediaItem(attachment);

                long totalAttachmentSize = messageAttachments.Sum(x => x.Size) + mediaItem.Size;
                if (totalAttachmentSize > GlobalSettings.AttachmentTotalSizeInBytes)
                {
                    response.ErrorMessage = EcmTexts.Localize(EcmTexts.TotalAttachmentSizeExceedsAllowedSize, data.FileName, StringUtil.GetSizeString(totalAttachmentSize), StringUtil.GetSizeString(GlobalSettings.AttachmentTotalSizeInBytes));
                    return response;
                }

                messageAttachments.Add(mediaItem);
                messageItem.Source.Attachments = messageAttachments;

                using (new EditContext(mediaItem.InnerItem))
                {
                    mediaItem.InnerItem.Name = "attachment" + DateUtil.IsoNowTime;
                    mediaItem.InnerItem.Appearance.DisplayName = new ItemUtilExt().SanitizeName(Path.GetFileNameWithoutExtension(data.FileName));
                    mediaItem.InnerItem.Fields["Title"].Value = data.FileName;
                }

                response.Error = false;
                response.ErrorMessage = EcmTexts.Localize(EcmTexts.TheFileXHasBeenAttachedToTheMessage, data.FileName);

                if (ProposeCopyToAllLanguages(messageItem, data.Language, data.FileName))
                {
                    response.NotificationMessages = new[]
                    {
                        new MessageBarMessageContext()
                        {
                            ActionLink = string.Format("trigger:attachment:file:addtoalllanguages({{\"attachmentId\":\"{0}\", \"fileName\":\"{1}\"}})", data.AttachmentId, data.FileName),
                            ActionText = EcmTexts.Localize(EcmTexts.ClickHere),
                            Message = EcmTexts.Localize(string.Format(EcmTexts.ToCopyTheNewlyAddedAttachmentsToAllMessageLanguageVersions, data.FileName))
                        }
                    };
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, e);
                response.Error = true;
                response.ErrorMessage = EcmTexts.Localize(EcmTexts.GeneralUIErrorMessage);
            }

            return response;
        }

        private bool ProposeCopyToAllLanguages(MessageItem messageItem, string contextLanguage, string fileName)
        {
            var contextLang = Globalization.Language.Parse(contextLanguage);

            var messageLanguages = _itemUtil.GetItemLanguages(messageItem.InnerItem).Where(lang => lang != contextLang).ToList();

            if (!messageLanguages.Any())
            {
                return false;
            }

            foreach (var language in messageLanguages)
            {
                if (GetAttachments(messageItem, language).Any(mediaItem => mediaItem.DisplayName == fileName))
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<MediaItem> GetAttachments([NotNull] MessageItem message, [NotNull] Globalization.Language language)
        {
            Assert.IsNotNull(message, "message");
            Assert.IsNotNull(language, "language");

            var localizedMessage = _factory.GetMessageItem(message.ID, language.Name);

            if (localizedMessage == null)
            {
                yield break;
            }

            foreach (var attachment in localizedMessage.Attachments)
            {
                yield return attachment;
            }
        }
    }
}
