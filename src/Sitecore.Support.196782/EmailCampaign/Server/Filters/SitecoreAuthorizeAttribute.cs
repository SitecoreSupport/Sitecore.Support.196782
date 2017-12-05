namespace Sitecore.Support.EmailCampaign.Server.Filters
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Web.Http;
    using System.Web.Http.Controllers;
    using Sitecore.Diagnostics;
    using Sitecore.Security.Accounts;

    /// <summary>
    /// Attribute to authorize Sitecore user.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Reviewed. Suppression is OK here.")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    internal sealed class SitecoreAuthorizeAttribute : AuthorizeAttribute
    {
        private static readonly ITicketManager TicketManager = new TicketManagerWrapper();

        internal interface ITicketManager
        {
            bool IsCurrentTicketValid();
        }

        public SitecoreAuthorizeAttribute(params string[] roles)
        {
            Roles = string.Join(",", roles);
        }

        /// <summary>
        /// Gets or sets a value indicating whether only admins are authenticated.
        /// </summary>
        /// <value><c>true</c> if only admins are authenticated; otherwise, <c>false</c>.</value>
        public bool AdminsOnly { get; set; }

        /// <summary>
        /// Determines whether the specified action context is authorized.
        /// </summary>
        /// <param name="actionContext">The action context.</param>
        /// <returns>
        /// True if user is administrator. Otherwise we rely on logic in base attribute.
        /// </returns>
        protected override bool IsAuthorized([NotNull] HttpActionContext actionContext)
        {
            Assert.ArgumentNotNull(actionContext, "actionContext");

            var baseAuthorized = base.IsAuthorized(actionContext) && (!this.AdminsOnly);
            var user = actionContext.ControllerContext.RequestContext.Principal as User;
            var thisAuthorized = user != null && user.IsAdministrator;
            var result = baseAuthorized || thisAuthorized;

            return result && TicketManager.IsCurrentTicketValid();
        }

        private class TicketManagerWrapper : ITicketManager
        {
            public bool IsCurrentTicketValid()
            {
                return Web.Authentication.TicketManager.IsCurrentTicketValid();
            }
        }
    }
}