﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sticky.Dto.Dashboard.Request;
using Sticky.Dto.Dashboard.Response;
using Sticky.Models.Context;
using Sticky.Models.Etc;
using Sticky.Repositories.Common.Extensions;
using Sticky.Repositories.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Sticky.Repositories.Dashboard.Implementions
{

    public class HostManager : IHostManager
    {

        private readonly StickyDbContext _db;
        private readonly IErrorLogger _errorLogger;
        private readonly DashboardAPISetting _setting;
        public HostManager(StickyDbContext db, IOptions<DashboardAPISetting> options, IErrorLogger errorLogger)
        {
            _errorLogger = errorLogger;
            _db = db;
            _setting = options.Value;
        }
        public async Task<HostResult> CreateAsync(string userId,CreateHostRequest request)
        {
            var fileLocation = _setting.ScriptLocation;
            var currentHost = _db.Hosts.FirstOrDefault(c => c.HostAddress == request.HostAddress);
            if (currentHost == null)
            {
                var hashCode = Guid.NewGuid().ToString();
                var newHost = new Host() { HostAddress = request.HostAddress, UserId = userId, HostValidated = true, PageValidated = false, HashCode = hashCode, UserValidityId = 9, ProductValidityId = 9 };
                await _db.Hosts.AddAsync(newHost);
                await _db.SaveChangesAsync();
                await _db.UsersHostAccess.AddAsync(new UsersHostAccess() { UserId = userId, AdminAccess = true, HostId = newHost.Id });
                await _db.SaveChangesAsync();
                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.Combine(fileLocation, request.HostAddress.RemoveSpecialCharacters())))
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(fileLocation, request.HostAddress.RemoveSpecialCharacters()));
                        System.IO.File.Copy(System.IO.Path.Combine(fileLocation , "track.js"),System.IO.Path.Combine(fileLocation , request.HostAddress.RemoveSpecialCharacters() , "track.js"));

                    }

                }
                catch (Exception ex)
                {
                    await _errorLogger.LogError("DashboardHostManager=>"+ex.Message);
                }
                return new HostResult() { Id = newHost.Id, TrackerAddress = _setting.ScriptAPIUrlBase + newHost.HostAddress.RemoveSpecialCharacters() + "/track.js", HostAddress = newHost.HostAddress };
            }
            else
                return new HostResult()
                {
                    Id = currentHost.Id,
                    TrackerAddress = "https://api.stickytracker.net/" + currentHost.HostAddress.RemoveSpecialCharacters() + "/track.js",
                    HostAddress = currentHost.HostAddress,
                    AlreadyExists = true,
                    SegmentCreationAccess = new SegmentCreationAccess()
                    {
                        Page = currentHost.HostValidated && currentHost.PageValidated,
                        AddToCart = currentHost.AddToCardValidated,
                        Buy = currentHost.FinalizeValidated,
                        ProductVisit = currentHost.CategoryValidated
                    }
                };

        }

        public async Task<bool> ModifyHostAsync(int id,string userId, ModifyHostRequest request)
        {
            if (!await UserHasAccessToHost(userId,id))
                return false;
            var row = await _db.Hosts.FirstOrDefaultAsync(c => c.Id == id);
            row.ProductValidityId = request.ProductValidityId;
            row.UserValidityId = request.UserValidityId;
            row.FinalizePage = request.FinalizeAddress;
            row.AddToCardId = request.AdToCartElementId;
            row.LogoAddress = request.LogoAddress;
            row.LogoOtherData = request.LogoOtherData;
            await _db.SaveChangesAsync();
            return true;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<IEnumerable<Host>> GetUserHostsAsync(string userId, int? id)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            if (id != null)
            {
                var host = _db.UsersHostAccess.Where(c => c.UserId == userId).Include(c => c.Host).Include(c => c.Host.User).Where(c => c.HostId == id).AsEnumerable().Select(c => c.Host);

                return host;
            }
            var hosts = _db.UsersHostAccess.Where(c => c.UserId == userId).Include(c => c.Host).Include(c => c.Host.User).AsEnumerable().Select(c => c.Host);
            return hosts;
        }

        public async Task<bool> GrantAccessToHostAsync(string userId, string destinationUserId, int hostId)
        {
            if(!await UserHasAccessToHost(userId,hostId))
                return false;
            try
            {
                var isExistNow = await _db.UsersHostAccess.FirstOrDefaultAsync(c => c.UserId == destinationUserId && c.HostId == hostId) == null;
                if (!isExistNow)
                    return true;
                await _db.UsersHostAccess.AddAsync(new UsersHostAccess() { AdminAccess = true, HostId = hostId, UserId = destinationUserId });
                await _db.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }




        }

        public async Task<IEnumerable<Host>> GetAllHostsAsync(string userId)
        {
            var userHosts = await _db.UsersHostAccess.Where(c => c.UserId == userId).Select(c => c.HostId).ToListAsync();
            var hosts = _db.Hosts.Where(c => c.HostValidated).Include(c => c.User).Include(c => c.Segments).Where(c => c.Segments.Any(d => d.IsPublic == true) || userHosts.Contains(c.Id)).AsEnumerable();
            return hosts;

        }

        public async Task<bool> UserHasAccessToHost(string userId, int host)
        {
            return await _db.UsersHostAccess.AnyAsync(c => c.UserId == userId && c.HostId == host);
        }
    }
}
