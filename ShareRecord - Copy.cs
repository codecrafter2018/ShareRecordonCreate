using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShareRecordonCreate
{
    public class ShareRecord : IPlugin
    {
        IOrganizationService _service = null;
        ITracingService _tracingService = null;
        IPluginExecutionContext context = null;
        Entity EntTarge = null;
        public void Execute(IServiceProvider serviceProvider)
        {
            context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            _service = (IOrganizationService)serviceFactory.CreateOrganizationService(context.InitiatingUserId);
            _tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            Guid UserId = context.InitiatingUserId;
            Guid entityId = context.PrimaryEntityId;
            EntTarge = (Entity)context.InputParameters["Target"];

            EntityReference entLookup = new EntityReference
            {
                Id = entityId,
                LogicalName = EntTarge.LogicalName
            };

          //  Entity entUser = GetUserDetails(UserId);
            EntityCollection entLeadGeographyMapping = GetLeadGeographyMapping(entityId);

            foreach (Entity entLeadMap in entLeadGeographyMapping.Entities)
            {
                if (entLeadMap.Contains("zox_region"))
                {
                    Guid regionId = ((EntityReference)entLeadMap["zox_region"]).Id;
                    _tracingService.Trace("Region Id is: " + regionId);
                                     
                    EntityCollection entCemBPDUsers = getCRMBPDUsersFromUserGeographyMapping(regionId, UserId);
                    _tracingService.Trace("entCemBPDUsers entity count is: " + entCemBPDUsers.Entities.Count);

                    foreach (Entity ent in entCemBPDUsers.Entities)
                    {
                        if (ent.Contains("zox_user"))
                            GrantAccess(entLookup, ((EntityReference)ent["zox_user"]));
                    }

                    getRMCUsersFromDepotMappingWithRMCRegion(regionId, UserId);            

                }

              else  if (entLeadMap.Contains("zox_pincode"))
                {
                    Guid pincodeId = ((EntityReference)entLeadMap["zox_pincode"]).Id;
                    _tracingService.Trace("PINCode Id is: " + pincodeId);

                    EntityCollection entCemBPDUsers = getCRMBPDUsersFromUserGeographyLineMapping(pincodeId, UserId);
                    _tracingService.Trace("GeographyLineMapping entUser entity count is: " + entCemBPDUsers.Entities.Count);

                    foreach (Entity ent in entCemBPDUsers.Entities)
                    {
                        if (ent.Contains("zox_user"))
                            GrantAccess(entLookup, ((EntityReference)ent["zox_user"]));
                    }

                    getRMCUsersFromRMCPINCodeMapping(pincodeId, UserId);

                }


            }
        }

        private void getRMCUsersFromRMCPINCodeMapping(Guid pincodeId, Guid userId)
        {
            var query = new QueryExpression("zox_rmcplantpincodemapping");
            query.Distinct = true;
            query.ColumnSet.AddColumns("zox_rmcplantpincodemappingid", "zox_plant", "zox_pincode");
            query.AddOrder("zox_name", OrderType.Ascending);
            query.Criteria.AddCondition("zox_pincode", ConditionOperator.Equal, pincodeId);
            EntityCollection RMCPINCodeMapping = _service.RetrieveMultiple(query);

            EntityReference entLookup = new EntityReference
            {
                Id = context.PrimaryEntityId,
                LogicalName = EntTarge.LogicalName
            };

            _tracingService.Trace("RMCPINCodeMapping entity count is: " + RMCPINCodeMapping.Entities.Count);
            foreach (Entity ent in RMCPINCodeMapping.Entities)
            {
                if (ent.Contains("zox_plant"))
                {
                    Guid plantId = ((EntityReference)ent["zox_plant"]).Id;

                    string fetchRecord = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>	" +
                             "<entity name='zox_usergeographylines'>" +
                                 "<attribute name='zox_usergeographylinesid' />" +
                                 "<attribute name='zox_name' />" +
                                 "<attribute name='createdon' />" +
                                 "<attribute name='zox_user' />" +
                                 "<order attribute='zox_name' descending='false' />" +
                                 "<filter type='and'>" +
                                     "<condition attribute='zox_city' operator='eq' value='" + plantId + "' />" +
                                     " </ filter > " +
                                 "<link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>" +
                                     "<filter type='and'>" +
                                         "<condition attribute='zox_lob' operator='in'>" +
                                             "<value> 100000002</value>" +
                                             "<value>100000000</value>" +
                                             "</condition> " +
                                         "<condition attribute='systemuserid' operator='ne'  value='" + userId + "' />" +
                                         "</filter> " +
                                     "</link-entity>" +
                                 "</entity> " +
                             "</fetch> ";

                    EntityCollection entUser = _service.RetrieveMultiple(new FetchExpression(fetchRecord));
                    _tracingService.Trace("getRMCUsersFromRMCPINCodeMapping entUser entity count is: " + entUser.Entities.Count);
                    foreach (Entity ent1 in entUser.Entities)
                    {
                        if (ent1.Contains("zox_user"))
                            GrantAccess(entLookup, ((EntityReference)ent1["zox_user"]));
                    }

                }
            }
        }

        private EntityCollection getCRMBPDUsersFromUserGeographyLineMapping(Guid pincodeId, Guid userId)
        {
            _tracingService.Trace("Into getCRMBPDUsersFromUserGeographyLineMapping");
            string fetchRecord = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>	" +
                              "<entity name='zox_usergeographylines'>" +
                                  "<attribute name='zox_usergeographylinesid' />" +
                                  "<attribute name='zox_name' />" +
                                  "<attribute name='createdon' />" +
                                  "<attribute name='zox_user' />" +
                                  "<order attribute='zox_name' descending='false' />" +
                                  "<filter type='and'>" +
                                      "<condition attribute='zox_pincode' operator='eq' value='" + pincodeId + "' />" +
                                      " </ filter > " +
                                  "<link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>" +
                                      "<filter type='and'>" +
                                          "<condition attribute='zox_lob' operator='in'>" +
                                              "<value> 100000002</value>" +
                                              "<value>100000000</value>" +
                                              "</condition> " +
                                          "<condition attribute='systemuserid' operator='ne'  value='" + userId + "' />" +
                                          "</filter> " +
                                      "</link-entity>" +
                                  "</entity> " +
                              "</fetch> ";

            EntityCollection entUser = _service.RetrieveMultiple(new FetchExpression(fetchRecord));
                   return entUser;
        }

        private void getRMCUsersFromDepotMappingWithRMCRegion(Guid regionId, Guid userId)
        {
            var query = new QueryExpression("zox_depotmappingwithrmcregion");
            query.Distinct = true;
            query.ColumnSet.AddColumns("zox_depotmappingwithrmcregionid", "zox_depot", "zox_region");
            query.AddOrder("zox_name", OrderType.Ascending);
            query.Criteria.AddCondition("zox_region", ConditionOperator.Equal, regionId);
            EntityCollection geographyMapping = _service.RetrieveMultiple(query);

            EntityReference entLookup = new EntityReference
            {
                Id = context.PrimaryEntityId,
                LogicalName = EntTarge.LogicalName
            };

            _tracingService.Trace("geographyMapping entity count is: " + geographyMapping.Entities.Count);
            foreach (Entity ent in geographyMapping.Entities)
            {
                if (ent.Contains("zox_depot"))
                {
                    Guid depotId = ((EntityReference)ent["zox_depot"]).Id;

                    string fetchRecord = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>" +
                                           "<entity name='zox_usergeographymapping'>" +
                                             "<attribute name='zox_usergeographymappingid' />" +
                                             "<attribute name='zox_name' />" +
                                             "<attribute name='createdon' />" +
                                              "<attribute name='zox_user' />" +
                                             "<order attribute='zox_name' descending='false' />" +
                                             "<filter type='and'>" +
                                               "<condition attribute='zox_depot' operator='eq' value='" + depotId + "' />" +
                                             "</filter>" +
                                             "<link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>" +
                                               "<filter type='and'>" +
                                                 "<condition attribute='zox_lob' operator='in'>" +
                                                   "<value> 100000002</value>" +
                                                     "<value>100000000</value>" +
                                                     "</condition>" +
                                                     "<condition attribute='systemuserid' operator='ne'  value='" + userId + "' />" +
                                                   "</filter>" +
                                                 "</link-entity>" +
                                               "</entity>" +
                                             "</fetch>";

                    EntityCollection entUser = _service.RetrieveMultiple(new FetchExpression(fetchRecord));
                    _tracingService.Trace("geographyMapping entUser entity count is: " + entUser.Entities.Count);
                    foreach (Entity ent1 in entUser.Entities)
                    {
                        if (ent1.Contains("zox_user"))
                            GrantAccess(entLookup, ((EntityReference)ent1["zox_user"]));
                    }

                }
            }
        }

        private EntityCollection GetLeadGeographyMapping(Guid entityId)
        {

            var query = new QueryExpression("zox_leadgeographymapping");
            query.Distinct = true;
            query.ColumnSet.AddColumns("zox_leadgeographymappingid", "zox_pincode", "zox_region");
            if (EntTarge.LogicalName == "zox_prelead")
                query.Criteria.AddCondition("zox_prelead", ConditionOperator.Equal, entityId);
            if (EntTarge.LogicalName == "opportunity")
                query.Criteria.AddCondition("zox_opportunity", ConditionOperator.Equal, entityId);
            if (EntTarge.LogicalName == "lead")
                query.Criteria.AddCondition("zox_lead", ConditionOperator.Equal, entityId);
            query.AddOrder("zox_name", OrderType.Ascending);
            EntityCollection geographyMapping = _service.RetrieveMultiple(query);
            return geographyMapping;

        }

        private void GrantAccess(EntityReference sharedRecord, EntityReference sharedUser)
        {
            var grantAccessRequest = new GrantAccessRequest
            {
                PrincipalAccess = new PrincipalAccess
                {
                    AccessMask = AccessRights.ReadAccess,
                    Principal = sharedUser
                },
                Target = sharedRecord
            };
            _service.Execute(grantAccessRequest);
        }

        private EntityCollection getCRMBPDUsersFromUserGeographyMapping(Guid regionId, Guid userId)
        {
            string fetchRecord = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>" +
                                   "<entity name='zox_usergeographymapping'>" +
                                     "<attribute name='zox_usergeographymappingid' />" +
                                     "<attribute name='zox_name' />" +
                                     "<attribute name='createdon' />" +
                                      "<attribute name='zox_user' />" +
                                     "<order attribute='zox_name' descending='false' />" +
                                     "<filter type='and'>" +
                                       "<condition attribute='zox_region' operator='eq' value='" + regionId + "' />" +
                                     "</filter>" +
                                     "<link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>" +
                                       "<filter type='and'>" +
                                         "<condition attribute='zox_lob' operator='in'>" +
                                           "<value> 100000002</value>" +
                                             "<value>100000000</value>" +
                                             "</condition>" +
                                             "<condition attribute='systemuserid' operator='ne'  value='" + userId + "' />" +
                                           "</filter>" +
                                         "</link-entity>" +
                                       "</entity>" +
                                     "</fetch>";

            EntityCollection entUser = _service.RetrieveMultiple(new FetchExpression(fetchRecord));
            return entUser;
        }

        private Entity GetUserDetails(Guid userId)
        {
            Entity entUser = _service.Retrieve("systemuser", userId, new ColumnSet("firstname", "zox_zone", "zox_lob", "zox_region", "zox_depot", "zox_taluka"));
            return entUser;
        }
    }
}
