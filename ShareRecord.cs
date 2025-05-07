using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareRecordOnCreate
{
    /// <summary>
    /// Plugin to share records (PreLead, Lead, Opportunity) with users based on geography (pincode/region) and segment.
    /// Executes on create/update of records to grant or revoke access.
    /// </summary>
    public class ShareRecord : IPlugin
    {
        // Services and context
        private IOrganizationService _service;
        private ITracingService _tracingService;
        private IPluginExecutionContext _context;

        // Entity references and images
        private EntityReference _entityLookup;
        private Entity _preImage;
        private Entity _postImage;
        private Entity _preLead;

        // User and entity identifiers
        private Guid _user1 = Guid.Empty;
        private Guid _user2 = Guid.Empty;
        private Guid _user3 = Guid.Empty;
        private Guid _user4 = Guid.Empty;
        private Guid _user5 = Guid.Empty;
        private Guid _user6 = Guid.Empty;
        private Guid _preLeadId = Guid.Empty;
        private Guid _segmentId = Guid.Empty;
        private Guid _initiatingUserId = Guid.Empty;
        private string _objectName = string.Empty;

        // Column sets for retrieving specific attributes
        private readonly ColumnSet _userColumns = new ColumnSet("systemuserid", "zox_segment", "zox_role");
        private readonly ColumnSet _preLeadColumns = new ColumnSet("zox_preleadid", "ownerid", "zox_ownerchange", "createdby");

        /// <summary>
        /// Main plugin execution method.
        /// </summary>
        /// <param name="serviceProvider">Service provider for accessing CRM services.</param>
        public void Execute(IServiceProvider serviceProvider)
        {
            try
            {
                // Initialize services and context
                InitializeServices(serviceProvider);

                // Retrieve environment variable for user context override
                string contexId = GetEnvironmentVariableValue("zox_UserID");

                // Override service context if a valid user ID is provided
                if (!string.IsNullOrEmpty(contexId) && Guid.TryParse(contexId, out Guid contextUserId))
                {
                    IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                    _service = serviceFactory.CreateOrganizationService(contextUserId);
                }

                // Validate plugin execution context
                if (_context.Depth >= 1 && !string.IsNullOrEmpty(contexId))
                {
                    _initiatingUserId = _context.InitiatingUserId;

                    // Retrieve pre and post images
                    _preImage = _context.PreEntityImages?.Contains("preImage") == true ? _context.PreEntityImages["preImage"] : null;
                    _postImage = _context.PostEntityImages?.Contains("postImage") == true ? _context.PostEntityImages["postImage"] : null;

                    // Determine the entity type and segment ID
                    SetEntityLookupAndObjectName();

                    // Handle pincode or region changes
                    HandleGeographyChanges();

                    // Grant access based on new pincode or region
                    GrantAccessBasedOnGeography();
                }
            }
            catch (Exception ex)
            {
                _tracingService?.Trace($"Error in Execute: {ex.Message}");
                throw new InvalidPluginExecutionException($"An error occurred in ShareRecord plugin: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Initializes CRM services and plugin execution context.
        /// </summary>
        private void InitializeServices(IServiceProvider serviceProvider)
        {
            _context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            _service = serviceFactory.CreateOrganizationService(_context.UserId);
            _tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        }

        /// <summary>
        /// Sets the entity lookup and object name based on post or pre-image data.
        /// </summary>
        private void SetEntityLookupAndObjectName()
        {
            if (_postImage != null)
            {
                if (_postImage.Contains("zox_prelead") && _postImage["zox_prelead"] != null)
                {
                    _entityLookup = (EntityReference)_postImage["zox_prelead"];
                    _preLeadId = _entityLookup.Id;
                    SetObjectNameAndSegmentId("zox_prelead", _entityLookup.Id);
                }
                else if (_postImage.Contains("zox_lead") && _postImage["zox_lead"] != null)
                {
                    _entityLookup = (EntityReference)_postImage["zox_lead"];
                    SetObjectNameAndSegmentId("lead", _entityLookup.Id);
                }
                else if (_postImage.Contains("zox_opportunity") && _postImage["zox_opportunity"] != null)
                {
                    _entityLookup = (EntityReference)_postImage["zox_opportunity"];
                    SetObjectNameAndSegmentId("opportunity", _entityLookup.Id);
                }
            }

            if (_preImage != null)
            {
                if (_preImage.Contains("zox_prelead")) _entityLookup = (EntityReference)_preImage["zox_prelead"];
                else if (_preImage.Contains("zox_lead")) _entityLookup = (EntityReference)_preImage["zox_lead"];
                else if (_preImage.Contains("zox_opportunity")) _entityLookup = (EntityReference)_preImage["zox_opportunity"];
            }
        }

        /// <summary>
        /// Sets object name and segment ID if not already set.
        /// </summary>
        private void SetObjectNameAndSegmentId(string entityName, Guid id)
        {
            if (string.IsNullOrEmpty(_objectName) && _segmentId == Guid.Empty)
            {
                _objectName = entityName;
                _segmentId = id;
            }
        }

        /// <summary>
        /// Handles changes in pincode or region, revoking access if necessary.
        /// </summary>
        private void HandleGeographyChanges()
        {
            if (_preImage == null || _postImage == null) return;

            if (_preImage.Contains("zox_pincode") && _postImage.Contains("zox_pincode"))
            {
                Guid prePincodeId = ((EntityReference)_preImage["zox_pincode"]).Id;
                Guid postPincodeId = ((EntityReference)_postImage["zox_pincode"]).Id;
                _tracingService.Trace($"Pincode comparison: Pre={prePincodeId}, Post={postPincodeId}");

                if (prePincodeId != postPincodeId && _segmentId != Guid.Empty && !string.IsNullOrEmpty(_objectName))
                {
                    RevokeAccessForPincodeChange(prePincodeId);
                }
            }
            else if (_preImage.Contains("zox_region") && _postImage.Contains("zox_region"))
            {
                Guid preRegionId = ((EntityReference)_preImage["zox_region"]).Id;
                Guid postRegionId = ((EntityReference)_postImage["zox_region"]).Id;
                _tracingService.Trace($"Region comparison: Pre={preRegionId}, Post={postRegionId}");

                if (preRegionId != postRegionId && _segmentId != Guid.Empty && !string.IsNullOrEmpty(_objectName))
                {
                    RevokeAccessForRegionChange(preRegionId);
                }
            }
        }

        /// <summary>
        /// Revokes access when pincode changes.
        /// </summary>
        private void RevokeAccessForPincodeChange(Guid pincodeId)
        {
            int segmentValue = GetSegmentValue(_segmentId, _objectName);
            EntityCollection users = GetCrmBpdUsersFromUserGeographyLineMapping(pincodeId, _initiatingUserId, segmentValue);
            _tracingService.Trace($"Revoking access for {users.Entities.Count} users for pincode {pincodeId}");

            foreach (Entity user in users.Entities)
            {
                if (user.Contains("zox_user"))
                {
                    RevokeAccess(_entityLookup, (EntityReference)user["zox_user"]);
                }
            }

            GetRmcUsersFromRmcPinCodeMapping(pincodeId, _initiatingUserId, "revoke");
        }

        /// <summary>
        /// Revokes access when region changes.
        /// </summary>
        private void RevokeAccessForRegionChange(Guid regionId)
        {
            int segmentValue = GetSegmentValue(_segmentId, _objectName);
            EntityCollection users = GetCrmBpdUsersFromUserGeographyMapping(regionId, _initiatingUserId, segmentValue);
            _tracingService.Trace($"Revoking access for {users.Entities.Count} users for region {regionId}");

            foreach (Entity user in users.Entities)
            {
                if (user.Contains("zox_user"))
                {
                    RevokeAccess(_entityLookup, (EntityReference)user["zox_user"]);
                }
            }

            GetRmcUsersFromDepotMappingWithRmcRegion(regionId, _initiatingUserId, "revoke");
        }

        /// <summary>
        /// Grants access to users based on the new pincode or region.
        /// </summary>
        private void GrantAccessBasedOnGeography()
        {
            if (_segmentId == Guid.Empty || string.IsNullOrEmpty(_objectName)) return;

            if (_postImage.Contains("zox_pincode"))
            {
                Guid pincodeId = ((EntityReference)_postImage["zox_pincode"]).Id;
                _tracingService.Trace($"Granting access for pincode {pincodeId}");
                GrantAccessForPincode(pincodeId);
            }
            else if (_postImage.Contains("zox_region"))
            {
                Guid regionId = ((EntityReference)_postImage["zox_region"]).Id;
                _tracingService.Trace($"Granting access for region {regionId}");
                GrantAccessForRegion(regionId);
            }
        }

        /// <summary>
        /// Grants access for users associated with a pincode.
        /// </summary>
        private void GrantAccessForPincode(Guid pincodeId)
        {
            int segmentValue = GetSegmentValue(_segmentId, _objectName);
            if (_preLeadId != Guid.Empty)
            {
                _preLead = _service.Retrieve("zox_prelead", _preLeadId, _preLeadColumns);
            }

            EntityCollection users = GetCrmBpdUsersFromUserGeographyLineMapping(pincodeId, _initiatingUserId, segmentValue);
            _tracingService.Trace($"Found {users.Entities.Count} users for pincode {pincodeId}");

            ProcessUsersForAccess(users, _preLead);

            if (_preLead?.GetAttributeValue<bool>("zox_ownerchange") == false)
            {
                UpdatePreLead(_preLeadId, _user1, _user2, _user3, _user4, _user5, _user6);
            }

            GetRmcUsersFromRmcPinCodeMapping(pincodeId, _initiatingUserId, "grant");
        }

        /// <summary>
        /// Grants access for users associated with a region.
        /// </summary>
        private void GrantAccessForRegion(Guid regionId)
        {
            int segmentValue = GetSegmentValue(_segmentId, _objectName);
            if (_preLeadId != Guid.Empty)
            {
                _preLead = _service.Retrieve("zox_prelead", _preLeadId, _preLeadColumns);
            }

            EntityCollection users = GetCrmBpdUsersFromUserGeographyMapping(regionId, _initiatingUserId, segmentValue);
            _tracingService.Trace($"Found {users.Entities.Count} users for region {regionId}");

            ProcessUsersForAccess(users, _preLead);

            if (_preLead?.GetAttributeValue<bool>("zox_ownerchange") == false)
            {
                UpdatePreLead(_preLeadId, _user1, _user2, _user3, _user4, _user5, _user6);
            }

            GetRmcUsersFromDepotMappingWithRmcRegion(regionId, _initiatingUserId, "grant");
        }

        /// <summary>
        /// Processes users for granting access and assigns roles.
        /// </summary>
        private void ProcessUsersForAccess(EntityCollection users, Entity preLead)
        {
            foreach (Entity entity in users.Entities)
            {
                if (!entity.Contains("zox_user")) continue;

                EntityReference userRef = (EntityReference)entity["zox_user"];
                if (preLead?.GetAttributeValue<bool>("zox_ownerchange") == false)
                {
                    AssignUserRoles(userRef);
                }

                GrantAccess(_entityLookup, userRef);
            }
        }

        /// <summary>
        /// Assigns user roles based on segment and role values.
        /// </summary>
        private void AssignUserRoles(EntityReference userRef)
        {
            Entity userDetail = _service.Retrieve("systemuser", userRef.Id, _userColumns);
            if (userDetail.Contains("zox_segment") && userDetail.Contains("zox_role"))
            {
                int segment = ((OptionSetValue)userDetail["zox_segment"]).Value;
                int role = ((OptionSetValue)userDetail["zox_role"]).Value;

                if (segment == 100000002 && role == 515140004) _user1 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role1
                else if (segment == 100000002 && role == 515140005) _user2 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role2
                else if (segment == 100000001 && role == 515140010) _user3 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role3
                else if (segment == 100000001 && role == 515140001) _user4 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role4
                else if (segment == 100000001 && role == 100000004) _user5 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role5
                else if (segment == 100000001 && role == 515140009) _user6 = userDetail.GetAttributeValue<Guid>("systemuserid"); // Role6
            }
        }

        /// <summary>
        /// Retrieves RMC users from RMC pincode mapping and grants or revokes access.
        /// </summary>
        private void GetRmcUsersFromRmcPinCodeMapping(Guid pincodeId, Guid userId, string action)
        {
            QueryExpression query = new QueryExpression("zox_rmcplantpincodemapping")
            {
                Distinct = true,
                ColumnSet = new ColumnSet("zox_rmcplantpincodemappingid", "zox_plant", "zox_pincode"),
                Criteria = { Conditions = { new ConditionExpression("zox_pincode", ConditionOperator.Equal, pincodeId) } },
                Orders = { new OrderExpression("zox_name", OrderType.Ascending) }
            };

            EntityCollection mappings = _service.RetrieveMultiple(query);
            _tracingService.Trace($"Found {mappings.Entities.Count} RMC pincode mappings");

            if (_preLeadId != Guid.Empty)
            {
                _preLead = _service.Retrieve("zox_prelead", _preLeadId, _preLeadColumns);
            }

            foreach (Entity mapping in mappings.Entities)
            {
                if (mapping.Contains("zox_plant"))
                {
                    Guid plantId = ((EntityReference)mapping["zox_plant"]).Id;
                    string fetchXml = BuildRmcPincodeFetchXml(plantId, userId);
                    ProcessRmcUsers(fetchXml, action);
                }
            }
        }

        /// <summary>
        /// Builds FetchXML for RMC pincode mapping users.
        /// </summary>
        private string BuildRmcPincodeFetchXml(Guid plantId, Guid userId)
        {
            return $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='zox_usergeographylines'>
                            <attribute name='zox_usergeographylinesid' />
                            <attribute name='zox_name' />
                            <attribute name='createdon' />
                            <attribute name='zox_user' />
                            <order attribute='zox_name' descending='false' />
                            <filter type='and'>
                                <condition attribute='zox_city' operator='eq' value='{plantId}' />
                            </filter>
                            <link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>
                                <filter type='and'>
                                    <condition attribute='zox_lob' operator='in'>
                                        <value>100000001</value>
                                    </condition>
                                    <condition attribute='zox_segment' operator='in'>
                                        <value>100000002</value>
                                        <value>100000001</value>
                                    </condition>
                                    <condition attribute='systemuserid' operator='ne' value='{userId}' />
                                </filter>
                            </link-entity>
                        </entity>
                    </fetch>";
        }

        /// <summary>
        /// Processes RMC users for granting or revoking access.
        /// </summary>
        private void ProcessRmcUsers(string fetchXml, string action)
        {
            EntityCollection users = _service.RetrieveMultiple(new FetchExpression(fetchXml));
            _tracingService.Trace($"Found {users.Entities.Count} RMC users");

            foreach (Entity user in users.Entities)
            {
                if (!user.Contains("zox_user")) continue;

                EntityReference userRef = (EntityReference)user["zox_user"];
                if (action == "grant")
                {
                    if (_preLead?.GetAttributeValue<bool>("zox_ownerchange") == false)
                    {
                        AssignUserRoles(userRef);
                    }
                    GrantAccess(_entityLookup, userRef);
                }
                else
                {
                    RevokeAccess(_entityLookup, userRef);
                }
            }

            if (_preLead?.GetAttributeValue<bool>("zox_ownerchange") == false)
            {
                UpdatePreLead(_preLeadId, _user1, _user2, _user3, _user4, _user5, _user6);
            }
        }

        /// <summary>
        /// Retrieves CRM BPD users from user geography line mapping.
        /// </summary>
        private EntityCollection GetCrmBpdUsersFromUserGeographyLineMapping(Guid pincodeId, Guid userId, int segmentValue)
        {
            string fetchXml = BuildGeographyLineFetchXml(pincodeId, userId, segmentValue);
            _tracingService.Trace($"Fetching users for pincode {pincodeId}, segment {segmentValue}");
            return _service.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// Builds FetchXML for geography line mapping users.
        /// </summary>
        private string BuildGeographyLineFetchXml(Guid pincodeId, Guid userId, int segmentValue)
        {
            string segmentFilter = segmentValue switch
            {
                100000001 => "<condition attribute='zox_segment' operator='in'><value>100000002</value></condition>",
                100000000 => "<condition attribute='zox_segment' operator='in'><value>100000001</value></condition>",
                _ => "<condition attribute='zox_segment' operator='in'><value>100000002</value><value>100000001</value></condition>"
            };

            return $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='zox_usergeographylines'>
                            <attribute name='zox_usergeographylinesid' />
                            <attribute name='zox_name' />
                            <attribute name='createdon' />
                            <attribute name='zox_user' />
                            <order attribute='zox_name' descending='false' />
                            <filter type='and'>
                                <condition attribute='zox_pincode' operator='eq' value='{pincodeId}' />
                            </filter>
                            <link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>
                                <filter type='and'>
                                    <condition attribute='zox_lob' operator='in'>
                                        <value>100000002</value>
                                        <value>100000000</value>
                                    </condition>
                                    {segmentFilter}
                                    <condition attribute='systemuserid' operator='ne' value='{userId}' />
                                </filter>
                            </link-entity>
                        </entity>
                    </fetch>";
        }

        /// <summary>
        /// Retrieves RMC users from depot mapping with RMC region.
        /// </summary>
        private void GetRmcUsersFromDepotMappingWithRmcRegion(Guid regionId, Guid userId, string action)
        {
            QueryExpression query = new QueryExpression("zox_depotmappingwithrmcregion")
            {
                Distinct = true,
                ColumnSet = new ColumnSet("zox_depotmappingwithrmcregionid", "zox_depot", "zox_region"),
                Criteria = { Conditions = { new ConditionExpression("zox_region", ConditionOperator.Equal, regionId) } },
                Orders = { new OrderExpression("zox_name", OrderType.Ascending) }
            };

            EntityCollection mappings = _service.RetrieveMultiple(query);
            _tracingService.Trace($"Found {mappings.Entities.Count} depot mappings");

            if (_preLeadId != Guid.Empty)
            {
                _preLead = _service.Retrieve("zox_prelead", _preLeadId, _preLeadColumns);
            }

            foreach (Entity mapping in mappings.Entities)
            {
                if (mapping.Contains("zox_depot"))
                {
                    Guid depotId = ((EntityReference)mapping["zox_depot"]).Id;
                    string fetchXml = BuildDepotMappingFetchXml(depotId, userId);
                    ProcessRmcUsers(fetchXml, action);
                }
            }
        }

        /// <summary>
        /// Builds FetchXML for depot mapping users.
        /// </summary>
        private string BuildDepotMappingFetchXml(Guid depotId, Guid userId)
        {
            return $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='zox_usergeographymapping'>
                            <attribute name='zox_usergeographymappingid' />
                            <attribute name='zox_name' />
                            <attribute name='createdon' />
                            <attribute name='zox_user' />
                            <order attribute='zox_name' descending='false' />
                            <filter type='and'>
                                <condition attribute='zox_depot' operator='eq' value='{depotId}' />
                            </filter>
                            <link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>
                                <filter type='and'>
                                    <condition attribute='zox_lob' operator='in'>
                                        <value>100000001</value>
                                    </condition>
                                    <condition attribute='zox_segment' operator='in'>
                                        <value>100000002</value>
                                        <value>100000001</value>
                                    </condition>
                                    <condition attribute='systemuserid' operator='ne' value='{userId}' />
                                </filter>
                            </link-entity>
                        </entity>
                    </fetch>";
        }

        /// <summary>
        /// Grants access to a record for a specific user.
        /// </summary>
        private void GrantAccess(EntityReference sharedRecord, EntityReference sharedUser)
        {
            var request = new GrantAccessRequest
            {
                PrincipalAccess = new PrincipalAccess
                {
                    AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendAccess | AccessRights.AppendToAccess,
                    Principal = sharedUser
                },
                Target = sharedRecord
            };
            _service.Execute(request);
            _tracingService.Trace($"Granted access to {sharedUser.Id} for record {sharedRecord.Id}");
        }

        /// <summary>
        /// Grants extended access (including assign and share) to a record.
        /// </summary>
        private void GrantAccessWithExtendedRights(EntityReference sharedRecord, EntityReference sharedUser)
        {
            var request = new GrantAccessRequest
            {
                PrincipalAccess = new PrincipalAccess
                {
                    AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendAccess |
                                 AccessRights.AppendToAccess | AccessRights.AssignAccess | AccessRights.ShareAccess,
                    Principal = sharedUser
                },
                Target = sharedRecord
            };
            _service.Execute(request);
            _tracingService.Trace($"Granted extended access to {sharedUser.Id} for record {sharedRecord.Id}");
        }

        /// <summary>
        /// Retrieves the value of an environment variable.
        /// </summary>
        private string GetEnvironmentVariableValue(string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName))
            {
                _tracingService.Trace("Environment variable schema name is empty.");
                return null;
            }

            QueryExpression query = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("defaultvalue"),
                Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } }
            };

            EntityCollection result = _service.RetrieveMultiple(query);
            return result.Entities.Any() ? result.Entities[0].GetAttributeValue<string>("defaultvalue") : null;
        }

        /// <summary>
        /// Retrieves the segment value for a given entity.
        /// </summary>
        private int GetSegmentValue(Guid id, string name)
        {
            if (id == Guid.Empty || string.IsNullOrEmpty(name)) return 0;

            Entity entity = _service.Retrieve(name, id, new ColumnSet("zox_segment"));
            return entity?.Attributes.Contains("zox_segment") == true
                ? ((OptionSetValue)entity["zox_segment"]).Value
                : 0;
        }

        /// <summary>
        /// Revokes access to a record for a specific user.
        /// </summary>
        private void RevokeAccess(EntityReference sharedRecord, EntityReference sharedUser)
        {
            var request = new RevokeAccessRequest
            {
                Revokee = sharedUser,
                Target = sharedRecord
            };
            _service.Execute(request);
            _tracingService.Trace($"Revoked access for {sharedUser.Id} from record {sharedRecord.Id}");
        }

        /// <summary>
        /// Retrieves CRM BPD users from user geography mapping.
        /// </summary>
        private EntityCollection GetCrmBpdUsersFromUserGeographyMapping(Guid regionId, Guid userId, int segmentValue)
        {
            string fetchXml = BuildGeographyMappingFetchXml(regionId, userId, segmentValue);
            _tracingService.Trace($"Fetching users for region {regionId}, segment {segmentValue}");
            return _service.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// Builds FetchXML for geography mapping users.
        /// </summary>
        private string BuildGeographyMappingFetchXml(Guid regionId, Guid userId, int segmentValue)
        {
            string segmentFilter = segmentValue switch
            {
                100000001 => "<condition attribute='zox_segment' operator='in'><value>100000002</value></condition>",
                100000000 => "<condition attribute='zox_segment' operator='in'><value>100000001</value></condition>",
                _ => "<condition attribute='zox_segment' operator='in'><value>100000002</value><value>100000001</value></condition>"
            };

            return $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='zox_usergeographymapping'>
                            <attribute name='zox_usergeographymappingid' />
                            <attribute name='zox_name' />
                            <attribute name='createdon' />
                            <attribute name='zox_user' />
                            <order attribute='zox_name' descending='false' />
                            <filter type='and'>
                                <condition attribute='zox_region' operator='eq' value='{regionId}' />
                            </filter>
                            <link-entity name='systemuser' from='systemuserid' to='zox_user' link-type='inner' alias='ab'>
                                <filter type='and'>
                                    <condition attribute='zox_lob' operator='in'>
                                        <value>100000002</value>
                                        <value>100000000</value>
                                    </condition>
                                    {segmentFilter}
                                    <condition attribute='systemuserid' operator='ne' value='{userId}' />
                                </filter>
                            </link-entity>
                        </entity>
                    </fetch>";
        }

        /// <summary>
        /// Updates the PreLead entity with the assigned owner and grants extended access.
        /// </summary>
        private void UpdatePreLead(Guid preLeadId, Guid user1, Guid user2, Guid user3, Guid user4, Guid user5, Guid user6)
        {
            if (preLeadId == Guid.Empty) return;

            _preLead = _service.Retrieve("zox_prelead", preLeadId, _preLeadColumns);
            bool ownerChanged = true;

            if (user1 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user1);
            else if (user2 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user2);
            else if (user3 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user3);
            else if (user4 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user4);
            else if (user5 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user5);
            else if (user6 != Guid.Empty) _preLead["ownerid"] = new EntityReference("systemuser", user6);

            if (_preLead.Contains("ownerid"))
            {
                _preLead["zox_ownerchange"] = ownerChanged;
                _service.Update(_preLead);
                _tracingService.Trace($"Updated PreLead {preLeadId} with new owner");

                // Grant extended access to initiating user and creator
                GrantAccessWithExtendedRights(_entityLookup, new EntityReference("systemuser", _initiatingUserId));
                if (_preLead.Contains("createdby"))
                {
                    GrantAccessWithExtendedRights(_entityLookup, (EntityReference)_preLead["createdby"]);
                }
            }
        }
    }
}