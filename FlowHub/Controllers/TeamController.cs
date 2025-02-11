﻿using FlowHub.Models;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using FlowHub.Common;
using System.Net.Http;
using System.Collections;
using System.Net;
using System.IO;
using FlowHub.ViewModels;
using FlowHub.Api_Managers;

namespace FlowHub.Controllers
{
    [Authorize]
    public class TeamController : Controller
    {
        private ApplicationDbContext _context;
        private static readonly FacebookPostsApi facebookPostsApi = new FacebookPostsApi();
        private static readonly TwitterPostsApi twitterPostsApi = new TwitterPostsApi();

        public TeamController()
        {
            _context = new ApplicationDbContext();
        }

        // GET: Team
        public ActionResult Index()
        {
            return View();
        }

        // GET: Team/NewTeam
        public ActionResult NewTeam()
        {
            return PartialView("~/Views/Team/Partials/_NewTeam.cshtml", new Team());
        }

        // POST: Team/Create
        [HttpPost]
        public ActionResult Create()
        {
            GetUser(out _, out ApplicationUser user);

            if (user.Team != null)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var form = Request.Form;

            Team team = new Team
            {
                Name = !String.IsNullOrEmpty(form["name"]) ? form["name"] : "MyTeam",
                Info = !String.IsNullOrEmpty(form["info"]) ? form["info"] : "No info",
                Avatar = Request.Files["logo"] == null ? "default.png" : Avatar.Upload(Request.Files["logo"]),
                Leader = user
            };

            user.Team = team;
            _context.Teams.Add(team);

            var emails = !String.IsNullOrEmpty(form["emails"])
                ? form["emails"].Split(new char[] { ',' }).ToList()
                : null;

            if (emails != null)
            {
                var users =
                    from u in _context.Users.AsEnumerable()
                    where emails.Contains(u.Email)
                    select u;

                foreach (var u in users)
                    u.Team = team;

                team.ApplicationUsers = users?.ToList();
            }

            _context.SaveChanges();

            return Json(new { redirectUrl = Url.Action("Team", "Dashboard") });
        }

        // GET: Team/Manage
        public async System.Threading.Tasks.Task<ActionResult> Manage(string tab)
        {
            GetUser(out _, out ApplicationUser user);
            GetSocialMediaAccounts(user, SocialMediaAccounts.Team,
                        out FacebookUserAccount facebookAccount,
                        out TwitterUserAccount twitterAccount);

            bool CanAccessFacebook = facebookAccount.account_access_token != "" &&
                await FacebookOAuthLogin.IsAuthorized(facebookAccount.account_access_token);
            bool CanAccessTwitter = twitterAccount.account_access_token != "" &&
                await TwitterOAuthAuthenticator.IsAuthorized(twitterAccount.account_access_token, twitterAccount.account_access_token_secret);

            List<SocialMediaAccountViewModel> profileInfo = new List<SocialMediaAccountViewModel>();

            try
            {
                if (CanAccessFacebook)
                {
                    profileInfo.Add(await facebookPostsApi.GetProfileInfoAsync(facebookAccount.AccountId, facebookAccount.account_access_token));
                }

                if (CanAccessTwitter)
                {
                    profileInfo.Add(await twitterPostsApi.GetProfileInfoAsync(twitterAccount.AccountId, twitterAccount.account_access_token, twitterAccount.account_access_token_secret));
                }
            }
            catch (SocialMediaApiException ex)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            switch (tab)
            {
                case "overview":
                    return PartialView("~/Views/Team/Partials/_Overview.cshtml", Tuple.Create(user, profileInfo));
                case "settings":
                    Dictionary<string, SocialMediaAccountViewModel> profileInfoDict = profileInfo.ToDictionary(p => p.Type.ToLower(), p => p);
                    //if (user.Team.LeaderId == user.Id)
                        return PartialView("~/Views/Team/Partials/_Settings.cshtml", Tuple.Create(user, profileInfoDict));
                    //else
                    //    return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
                default:
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
        }

        // POST: Team/Update
        [HttpPost]
        public ActionResult Update()
        {
            GetUser(out _, out ApplicationUser user);

            if (user.Team.LeaderId != user.Id)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var team = user.Team;

            if (team == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var form = Request.Form;

            if (!String.IsNullOrEmpty(form["name"]))
                team.Name = form["name"];
            if (!String.IsNullOrEmpty(form["info"]))
                team.Info = form["info"];
            if (Request.Files["logo"] != null)
            {
                Avatar.Delete(team.Avatar);
                team.Avatar = Avatar.Upload(Request.Files["logo"]);
            }

            _context.SaveChanges();

            return Json(new
            {
                team.Name,
                team.Info,
                LogoURL = Path.Combine("/Avatars/", team.Avatar)
            });
        }

        // POST: Team/Delete
        [HttpPost]
        public ActionResult Delete()
        {
            GetUser(out _, out ApplicationUser user);

            if (user.Team.LeaderId != user.Id)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var team = user.Team;

            if (team == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            Avatar.Delete(team.Avatar);

            if (user.twitterTeamAccount != null)
                _context.TwitterTeamAccounts.Remove(user.twitterTeamAccount);

            if (user.FbTeamAccount != null)
                _context.FacebookTeamAccounts.Remove(user.FbTeamAccount);

            foreach (var u in team.ApplicationUsers)
            {
                if (u.twitterTeamAccount != null)
                    _context.TwitterTeamAccounts.Remove(u.twitterTeamAccount);

                if (u.FbTeamAccount != null)
                    _context.FacebookTeamAccounts.Remove(u.FbTeamAccount);
            }

            user.Team = null;
            team.Leader = null;
            //team.ApplicationUsers = null;
            foreach (var member in team.ApplicationUsers?.ToList())
                member.Team = null;

            _context.Teams.Remove(team);
            _context.SaveChanges();

            return Json(new { redirectUrl = Url.Action("Index", "Dashboard") });
        }

        // POST: Team/AddMembers
        [HttpPost]
        public ActionResult AddMembers()
        {
            GetUser(out _, out ApplicationUser user);

            if (user.Id != user.Team.LeaderId)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var form = Request.Form;
            var emails = !String.IsNullOrEmpty(form["emails"])
                ? form["emails"].Split(new char[] { ',' }).ToList()
                : null;

            if (emails == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var users =
                from u in _context.Users.AsEnumerable()
                where emails.Contains(u.Email)
                select u;

            foreach (var u in users)
                u.Team = user.Team;

            user.Team.ApplicationUsers = user.Team.ApplicationUsers?.Union(second: users).ToList();

            _context.SaveChanges();

            var members = new List<UserViewModel>();
            foreach (var member in user.Team.ApplicationUsers)
                members.Add(new UserViewModel
                {
                    Avatar = member.Avatar,
                    FullName = member.Name + " " + member.Surname,
                    Email = member.Email
                });

            return Json(members?.ToArray());
        }

        // POST: Team.RemoveMembers
        [HttpPost]
        public ActionResult RemoveMembers()
        {
            GetUser(out _, out ApplicationUser user);

            if (user.Id != user.Team.LeaderId)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var form = Request.Form;
            var emails = !String.IsNullOrEmpty(form["emails"])
                ? form["emails"].Split(new char[] { ',' }).ToList()
                : null;

            if (emails == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var users =
                from u in _context.Users.AsEnumerable()
                where emails.Contains(u.Email)
                select u;

            foreach (var u in users)
                u.Team = null;

            user.Team.ApplicationUsers = user.Team.ApplicationUsers?.Except(second: users).ToList();

            _context.SaveChanges();

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_context != null)
                {
                    _context.Dispose();
                    _context = null;
                }
            }

            base.Dispose(disposing);
        }

        #region helpers
        private void GetUser(out string id, out ApplicationUser user)
        {
            id = User.Identity.GetUserId();
            user = _context.Users.Find(id);
        }

        enum SocialMediaAccounts
        {
            User,
            Team
        }

        private void GetSocialMediaAccounts(ApplicationUser user, SocialMediaAccounts accountType,
            out FacebookUserAccount facebookAccount,
            out TwitterUserAccount twitterAccount)
        {
            facebookAccount = new FacebookUserAccount();
            twitterAccount = new TwitterUserAccount();

            if (accountType == SocialMediaAccounts.Team)
            {
                facebookAccount.AccountId = user.FbTeamAccount != null ? user.FbTeamAccount.AccountId : "";
                facebookAccount.account_access_token = user.FbTeamAccount != null ? user.FbTeamAccount.account_access_token : "";

                twitterAccount.AccountId = user.twitterTeamAccount != null ? user.twitterTeamAccount.AccountId : "";
                twitterAccount.account_access_token = user.twitterTeamAccount != null ? user.twitterTeamAccount.account_access_token : "";
                twitterAccount.account_access_token_secret = user.twitterTeamAccount != null ? user.twitterTeamAccount.account_access_token_secret : "";

                return;
            }

            facebookAccount.AccountId = user.FbUserAccount != null ? user.FbUserAccount.AccountId : "";
            facebookAccount.account_access_token = user.FbUserAccount != null ? user.FbUserAccount.account_access_token : "";

            twitterAccount.AccountId = user.twitterUserAccount != null ? user.twitterUserAccount.AccountId : "";
            twitterAccount.account_access_token = user.twitterUserAccount != null ? user.twitterUserAccount.account_access_token : "";
            twitterAccount.account_access_token_secret = user.twitterUserAccount != null ? user.twitterUserAccount.account_access_token_secret : "";
        }
        #endregion
    }
}