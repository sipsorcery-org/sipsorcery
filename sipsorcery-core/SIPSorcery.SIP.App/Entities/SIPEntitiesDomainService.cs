
namespace SIPSorcery.SIP.App.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Data;
    using System.Linq;
    using System.ServiceModel.DomainServices.EntityFramework;
    using System.ServiceModel.DomainServices.Hosting;
    using System.ServiceModel.DomainServices.Server;


    // Implements application logic using the SIPSorceryAppEntities context.
    // TODO: Add your application logic to these methods or in additional methods.
    // TODO: Wire up authentication (Windows/ASP.NET Forms) and uncomment the following to disable anonymous access
    // Also consider adding roles to restrict access as appropriate.
    // [RequiresAuthentication]
    [EnableClientAccess()]
    public class SIPEntitiesDomainService : LinqToEntitiesDomainService<SIPSorceryAppEntities>
    {

        // TODO:
        // Consider constraining the results of your query method.  If you need additional input you can
        // add parameters to this method or create additional query methods with different names.
        // To support paging you will need to add ordering to the 'SIPDialplanLookups' query.
        public IQueryable<SIPDialplanLookup> GetSIPDialplanLookups()
        {
            return this.ObjectContext.SIPDialplanLookups;
        }

        public void InsertSIPDialplanLookup(SIPDialplanLookup sIPDialplanLookup)
        {
            if ((sIPDialplanLookup.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sIPDialplanLookup, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanLookups.AddObject(sIPDialplanLookup);
            }
        }

        public void UpdateSIPDialplanLookup(SIPDialplanLookup currentSIPDialplanLookup)
        {
            this.ObjectContext.SIPDialplanLookups.AttachAsModified(currentSIPDialplanLookup, this.ChangeSet.GetOriginal(currentSIPDialplanLookup));
        }

        public void DeleteSIPDialplanLookup(SIPDialplanLookup sIPDialplanLookup)
        {
            if ((sIPDialplanLookup.EntityState == EntityState.Detached))
            {
                this.ObjectContext.SIPDialplanLookups.Attach(sIPDialplanLookup);
            }
            this.ObjectContext.SIPDialplanLookups.DeleteObject(sIPDialplanLookup);
        }

        // TODO:
        // Consider constraining the results of your query method.  If you need additional input you can
        // add parameters to this method or create additional query methods with different names.
        // To support paging you will need to add ordering to the 'SIPDialplanOptions' query.
        public IQueryable<SIPDialplanOption> GetSIPDialplanOptions()
        {
            return this.ObjectContext.SIPDialplanOptions;
        }

        public void InsertSIPDialplanOption(SIPDialplanOption sIPDialplanOption)
        {
            if ((sIPDialplanOption.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sIPDialplanOption, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanOptions.AddObject(sIPDialplanOption);
            }
        }

        public void UpdateSIPDialplanOption(SIPDialplanOption currentSIPDialplanOption)
        {
            this.ObjectContext.SIPDialplanOptions.AttachAsModified(currentSIPDialplanOption, this.ChangeSet.GetOriginal(currentSIPDialplanOption));
        }

        public void DeleteSIPDialplanOption(SIPDialplanOption sIPDialplanOption)
        {
            if ((sIPDialplanOption.EntityState == EntityState.Detached))
            {
                this.ObjectContext.SIPDialplanOptions.Attach(sIPDialplanOption);
            }
            this.ObjectContext.SIPDialplanOptions.DeleteObject(sIPDialplanOption);
        }

        // TODO:
        // Consider constraining the results of your query method.  If you need additional input you can
        // add parameters to this method or create additional query methods with different names.
        // To support paging you will need to add ordering to the 'SIPDialplanProviders' query.
        public IQueryable<SIPDialplanProvider> GetSIPDialplanProviders()
        {
            return this.ObjectContext.SIPDialplanProviders;
        }

        public void InsertSIPDialplanProvider(SIPDialplanProvider sIPDialplanProvider)
        {
            if ((sIPDialplanProvider.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sIPDialplanProvider, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanProviders.AddObject(sIPDialplanProvider);
            }
        }

        public void UpdateSIPDialplanProvider(SIPDialplanProvider currentSIPDialplanProvider)
        {
            this.ObjectContext.SIPDialplanProviders.AttachAsModified(currentSIPDialplanProvider, this.ChangeSet.GetOriginal(currentSIPDialplanProvider));
        }

        public void DeleteSIPDialplanProvider(SIPDialplanProvider sIPDialplanProvider)
        {
            if ((sIPDialplanProvider.EntityState == EntityState.Detached))
            {
                this.ObjectContext.SIPDialplanProviders.Attach(sIPDialplanProvider);
            }
            this.ObjectContext.SIPDialplanProviders.DeleteObject(sIPDialplanProvider);
        }

        // TODO:
        // Consider constraining the results of your query method.  If you need additional input you can
        // add parameters to this method or create additional query methods with different names.
        // To support paging you will need to add ordering to the 'SIPDialplanRoutes' query.
        public IQueryable<SIPDialplanRoute> GetSIPDialplanRoutes()
        {
            return this.ObjectContext.SIPDialplanRoutes;
        }

        public void InsertSIPDialplanRoute(SIPDialplanRoute sIPDialplanRoute)
        {
            if ((sIPDialplanRoute.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sIPDialplanRoute, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanRoutes.AddObject(sIPDialplanRoute);
            }
        }

        public void UpdateSIPDialplanRoute(SIPDialplanRoute currentSIPDialplanRoute)
        {
            this.ObjectContext.SIPDialplanRoutes.AttachAsModified(currentSIPDialplanRoute, this.ChangeSet.GetOriginal(currentSIPDialplanRoute));
        }

        public void DeleteSIPDialplanRoute(SIPDialplanRoute sIPDialplanRoute)
        {
            if ((sIPDialplanRoute.EntityState == EntityState.Detached))
            {
                this.ObjectContext.SIPDialplanRoutes.Attach(sIPDialplanRoute);
            }
            this.ObjectContext.SIPDialplanRoutes.DeleteObject(sIPDialplanRoute);
        }
    }
}


