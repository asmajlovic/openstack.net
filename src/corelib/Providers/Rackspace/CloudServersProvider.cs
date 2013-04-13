﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JSIStudios.SimpleRESTServices.Client;
using JSIStudios.SimpleRESTServices.Client.Json;
using net.openstack.Core;
using net.openstack.Core.Domain;
using net.openstack.Core.Domain.Mapping;
using net.openstack.Providers.Rackspace.Objects.Request;
using net.openstack.Providers.Rackspace.Objects.Response;

namespace net.openstack.Providers.Rackspace
{
    public class CloudServersProvider : ProviderBase, ICloudServersProvider
    {
        private readonly int[] _validServerActionResponseCode = new[] { 200, 202, 203, 204 };
        private readonly IJsonObjectMapper<Network> _networkResponseMapper;

        #region Constructors

        /// <summary>
        /// Creates a new instance of the Rackspace <see cref="net.openstack.Providers.Rackspace.CloudServersProvider"/> class.
        /// </summary>
        public CloudServersProvider()
            : this(null) { }

        /// <summary>
        /// Creates a new instance of the Rackspace <see cref="net.openstack.Providers.Rackspace.CloudServersProvider"/> class.
        /// </summary>
        /// <param name="identity">An instance of a <see cref="net.openstack.Core.Domain.CloudIdentity"/> object.<remarks>[Optional]: If not provided, the user will be required to pass a <see cref="net.openstack.Core.Domain.CloudIdentity"/> object to each method individually.</remarks></param>
        public CloudServersProvider(CloudIdentity identity)
            : this(identity, new CloudIdentityProvider(), new JsonRestServices()) { }

        internal CloudServersProvider(ICloudIdentityProvider identityProvider, IRestService restService)
            : this(null, identityProvider, restService) { }

        internal CloudServersProvider(ICloudIdentityProvider identityProvider, IRestService restService, IJsonObjectMapper<Network> networkResponseMapper) 
            : this(null, identityProvider, restService, networkResponseMapper){}

        internal CloudServersProvider(CloudIdentity identity, ICloudIdentityProvider identityProvider, IRestService restService)
            : this(identity, identityProvider, restService, new NetworkResponseJsonMapper()) { }

        internal CloudServersProvider(CloudIdentity identity, ICloudIdentityProvider identityProvider, IRestService restService, IJsonObjectMapper<Network> networkResponseMapper)
            : base(identity, identityProvider, restService)
        {
            _networkResponseMapper = networkResponseMapper;
        }

        #endregion

        #region Servers

        public IEnumerable<Server> ListServers(string imageId = null, string flavorId = null, string name = null, string status = null, string markerId = null, int? limit = null, DateTime? changesSince = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers", GetServiceEndpoint(identity, region)));

            var response = ExecuteRESTRequest<ListServersResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Servers;
        }

        public IEnumerable<ServerDetails> ListServersWithDetails(string imageId = null, string flavorId = null, string name = null, string status = null, string markerId = null, int? limit = null, DateTime? changesSince = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/detail", GetServiceEndpoint(identity, region)));
            
            var response = ExecuteRESTRequest<ListServersResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Servers;
        }

        public NewServer CreateServer(string cloudServerName, string imageName, string flavor, string diskConfig = null, Metadata metadata = null, string personality = null, bool attachToServiceNetwork = false, bool attachToPublicNetwork = false, IEnumerable<Guid> networks = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers", GetServiceEndpoint(identity, region)));

            NewServerNetwork[] networksToAttach = null;

            if (attachToServiceNetwork || attachToPublicNetwork)
            {
                var networkList = new List<NewServerNetwork>();

                if(attachToPublicNetwork)
                    networkList.Add(new NewServerNetwork { Id = new Guid("00000000-0000-0000-0000-000000000000") });

                if(attachToServiceNetwork)
                    networkList.Add(new NewServerNetwork { Id = new Guid("11111111-1111-1111-1111-111111111111") });

                if(networks != null && networks.Any())
                    networkList.AddRange(networks.Select(id => new NewServerNetwork(){Id = id}));

                networksToAttach = networkList.ToArray();
            }

            var request = new CreateServerRequest
                              {
                                  Details = new CreateServerDetails
                                                {
                                                    Name = cloudServerName,
                                                    DiskConfig = diskConfig,
                                                    Flavor = flavor,
                                                    ImageName = imageName,
                                                    Metadata = metadata,
                                                    Networks = networksToAttach,
                                                    Personality = personality,
                                                }
                              };
            var response = ExecuteRESTRequest<CreateServerResponse>(identity, urlPath, HttpMethod.POST, request);

            if (response == null || response.Data == null || response.Data.Server == null)
                return null;

            if (response.StatusCode != 200 && response.StatusCode != 202)
                return null; // throw new ExternalServiceException(response.StatusCode, response.Status, response.RawBody);
            
            return response.Data.Server;
        }

        public ServerDetails GetDetails(string cloudServerId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}", GetServiceEndpoint(identity, region), cloudServerId));

            var response = ExecuteRESTRequest<ServerDetailsResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null || response.Data.Server == null)
                return null;

            return response.Data.Server;
        }

        public bool UpdateServer(string cloudServerId, string name= null, string accessIPv4 = null, string accessIPv6 = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}", GetServiceEndpoint(identity, region), cloudServerId));

            var requestJson = new UpdateServerRequest(name, accessIPv4, accessIPv6);
            var response = ExecuteRESTRequest<ServerDetailsResponse>(identity, urlPath, HttpMethod.PUT, requestJson);

            if (response == null || response.Data == null || response.Data.Server == null)
                return false;

            if (response.StatusCode != 200 && response.StatusCode != 202)
                return false; 

            return true;
        }

        public bool DeleteServer(string cloudServerId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}", GetServiceEndpoint(identity, region), cloudServerId));

            var defaultSettings = BuildDefaultRequestSettings(new [] {404});
            var response = ExecuteRESTRequest<object>(identity, urlPath, HttpMethod.DELETE, settings: defaultSettings);

            if (response == null || !_validServerActionResponseCode.Contains(response.StatusCode))
                return false; // throw new ExternalServiceException(response.StatusCode, response.Status, response.RawBody);

            return true;
        }

        public ServerDetails WaitForServerState(string serverId, string expectedState, string[] errorStates, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            return WaitForServerState(serverId, new[] { expectedState }, errorStates, refreshCount, refreshDelayInMS, region, identity);
        }

        public ServerDetails WaitForServerState(string serverId, string[] expectedStates, string[] errorStates, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            var serverDetails = GetDetails(serverId, region, identity);

            int count = 0;
            while (!expectedStates.Contains(serverDetails.Status) && !errorStates.Contains(serverDetails.Status) && count < refreshCount)
            {
                Thread.Sleep(refreshDelayInMS);
                serverDetails = GetDetails(serverId, region, identity);
                count++;
            }

            if (errorStates.Contains(serverDetails.Status))
                throw new ServerEnteredErrorStateException(serverDetails.Status);

            return serverDetails;
        }

        public ServerDetails WaitForServerActive(string serverId, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            return WaitForServerState(serverId, ServerState.ACTIVE, new[] { ServerState.ERROR, ServerState.UNKNOWN, ServerState.SUSPENDED }, refreshCount, refreshDelayInMS, region, identity);
        }

        public void WaitForServerDeleted(string serverId, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            try
            {
                WaitForServerState(serverId, ServerState.DELETED,
                                   new[] {ServerState.ERROR, ServerState.UNKNOWN, ServerState.SUSPENDED},
                                   refreshCount, refreshDelayInMS, region, identity);
            }
            catch (Core.Exceptions.Response.ItemNotFoundException){} // there is the possibility that the server can be ACTIVE for one pass and then 
                                                                                   // by the next pass a 404 is returned.  This is due to the VERY limited window in which
                                                                                   // the server goes into the DELETED state before it is removed from the system.
        }

        #endregion

        #region Server Addresses

        public ServerAddresses ListAddresses(string serverId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/ips", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest<ListAddressesResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Addresses;
        }

        public IEnumerable<AddressDetails> ListAddressesByNetwork(string serverId, string network, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/ips/{2}", GetServiceEndpoint(identity, region), serverId, network));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.GET);

            if (response == null)
                return null;

            var data = _networkResponseMapper.Map(response.RawBody);

            return data.Addresses;
        }

        #endregion

        #region Server Actions

        public bool ChangeAdministratorPassword(string serverId, string password, string region = null, CloudIdentity identity = null)
        {
            var request = new ChangeServerAdminPasswordRequest { Details = new ChangeAdminPasswordDetails { AdminPassword = password } };
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        public bool RebootServer(string serverId, RebootType rebootType, string region = null, CloudIdentity identity = null)
        {
            var request = new ServerRebootRequest {Details = new ServerRebootDetails {Type = rebootType.ToString()}};
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        public ServerDetails RebuildServer(string serverId, string serverName, string imageName, string flavor, string adminPassword, string accessIPv4 = null, string accessIPv6 = null, Metadata metadata = null, string diskConfig = null, Personality personality = null, string region = null, CloudIdentity identity = null)
        {
            var request = new ServerRebuildRequest { Details = new ServerRebuildDetails
                                                                   {
                                                                       Name = serverName,
                                                                       ImageName = imageName,
                                                                       Flavor = flavor,
                                                                       DiskConfig = diskConfig,
                                                                       AdminPassword = adminPassword,
                                                                       Metadata = metadata,
                                                                       Personality = personality,
                                                                       AccessIPv4 = accessIPv4,
                                                                       AccessIPv6 = accessIPv6,
                                                                   } };
            var resp = ExecuteServerAction<ServerDetailsResponse>(serverId, request, region, identity);

            return resp.Server;
        }

        public bool ResizeServer(string serverId, string serverName, string flavor, string diskConfig = null, string region = null, CloudIdentity identity = null)
        {
            var request = new ServerResizeRequest
                {
                    Details = new ServerResizeDetails
                    {
                        Name = serverName,
                        Flavor = flavor,
                        DiskConfig = diskConfig,
                    }
                };
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        public bool ConfirmServerResize(string serverId, string region = null, CloudIdentity identity = null)
        {
            var request = new ConfirmServerResizeRequest();
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        public bool RevertServerResize(string serverId, string region = null, CloudIdentity identity = null)
        {
            var request = new RevertServerResizeRequest();
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        public string RescueServer(string serverId, string region = null, CloudIdentity identity = null)
        {
            var request = new RescueServerRequest{Details = "none"};
            var resp = ExecuteServerAction<RescueServerResponse>(serverId, request, region, identity);

            return resp.AdminPassword;
        }

        public ServerDetails UnRescueServer(string serverId, string region = null, CloudIdentity identity = null)
        {
            var request = new UnrescueServerRequest { Details = "none" };
            var resp = ExecuteServerAction<ServerDetailsResponse>(serverId, request, region, identity);

            return resp == null ? null : resp.Server;
        }

        public bool CreateImage(string serverId, string imageName, Metadata metadata = null, string region = null, CloudIdentity identity = null)
        {
            var request = new CreateServerImageRequest { Details = new CreateServerImageDetails{ImageName = imageName, Metadata = metadata} };
            var resp = ExecuteServerAction(serverId, request, region, identity);

            return resp;
        }

        private T ExecuteServerAction<T>(string serverId, object body, string region = null, CloudIdentity identity = null) where T : new()
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/action", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest<T>(identity, urlPath, HttpMethod.POST, body);

            if (response == null || response.Data == null || !_validServerActionResponseCode.Contains(response.StatusCode))
                return default(T);

            return response.Data;
        }

        private bool ExecuteServerAction(string serverId, object body, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/action", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.POST, body);

            if (response == null || !_validServerActionResponseCode.Contains(response.StatusCode))
                return false;

            return true;
        }

        #endregion

        #region Volume Attachment Actions

        public ServerVolume AttachServerVolume(string serverId, string volumeId, string storageDevice = null, string region = null,
                                               CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/os-volume_attachments", GetServiceEndpoint(identity, region), serverId));

            var request = new AttachServerVolumeRequest { ServerVolumeData = new AttachServerVolumeData { Device = storageDevice, VolumeId = volumeId } };
            var response = ExecuteRESTRequest<ServerVolumeResponse>(identity, urlPath, HttpMethod.POST, request);

            if (response == null || response.Data == null)
                return null;

            return response.Data.ServerVolume;
        }

        public IEnumerable<ServerVolume> ListServerVolumes(string serverId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/os-volume_attachments", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest<ServerVolumeListResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.ServerVolumes;
        }

        public ServerVolume GetServerVolumeDetails(string serverId, string volumeId, string region = null,
                                                   CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/os-volume_attachments/{2}", GetServiceEndpoint(identity, region), serverId, volumeId));

            var response = ExecuteRESTRequest<ServerVolumeResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.ServerVolume;
        }

        public bool DetachServerVolume(string serverId, string volumeId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/os-volume_attachments/{2}", GetServiceEndpoint(identity, region), serverId, volumeId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.DELETE);

            if (response == null || !_validServerActionResponseCode.Contains(response.StatusCode))
                return false;

            return true;
        }

        #endregion

        #region Flavors

        public IEnumerable<Flavor> ListFlavors(int minDiskInGB = 0, int minRamInMB = 0, string markerId = null, int limit = 0, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/flavors", GetServiceEndpoint(identity, region)));

            var response = ExecuteRESTRequest<ListFlavorsResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Flavors;
        }

        public IEnumerable<FlavorDetails> ListFlavorsWithDetails(int minDiskInGB = 0, int minRamInMB = 0, string markerId = null, int limit = 0, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/flavors/detail", GetServiceEndpoint(identity, region)));

            var response = ExecuteRESTRequest<ListFlavorDetailsResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Flavors;
        }

        public FlavorDetails GetFlavor(string id, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/flavors/{1}", GetServiceEndpoint(identity, region), id));

            var response = ExecuteRESTRequest<FlavorDetailsResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Flavor;
        }

        #endregion

        #region Images

        public IEnumerable<ServerImage> ListImages(string server = null, string imageName = null, string imageStatus = null, DateTime changesSince = new DateTime(), string markerId = null, int limit = 0, string imageType = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images", GetServiceEndpoint(identity, region)));

            var queryStringParameters = BuildListImagesQueryStringParameters(server, imageName, imageStatus, changesSince, markerId, limit, imageType);

            var response = ExecuteRESTRequest<ListImagesResponse>(identity, urlPath, HttpMethod.GET, queryStringParameter: queryStringParameters);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Images;
        }

        public IEnumerable<ServerImageDetails> ListImagesWithDetails(string server = null, string imageName = null, string imageStatus = null, DateTime changesSince = default(DateTime), string markerId = null, int limit = 0, string imageType = null, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/detail", GetServiceEndpoint(identity, region)));

            var queryStringParameters = BuildListImagesQueryStringParameters(server, imageName, imageStatus, changesSince, markerId, limit, imageType);

            var response = ExecuteRESTRequest<ListImagesDetailsResponse>(identity, urlPath, HttpMethod.GET, queryStringParameter: queryStringParameters);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Images;
        }

        private Dictionary<string, string> BuildListImagesQueryStringParameters(string serverId = null, string imageName = null, string imageStatus = null, DateTime changesSince = default(DateTime), string markerId = null, int limit = 0, string imageType = null)
        {
            var queryParameters = new Dictionary<string, string>();

            if(!string.IsNullOrWhiteSpace(serverId))
                queryParameters.Add("server", serverId);

            if (!string.IsNullOrWhiteSpace(imageName))
                queryParameters.Add("name", imageName);

            if (!string.IsNullOrWhiteSpace(imageStatus))
                queryParameters.Add("status", imageStatus);

            if (changesSince != default(DateTime))
                queryParameters.Add("changes-since", changesSince.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

            if (!string.IsNullOrWhiteSpace(markerId))
                queryParameters.Add("marker", markerId);

            if (limit > 0)
                queryParameters.Add("limit", limit.ToString());

            if(!string.IsNullOrWhiteSpace(imageType))
                queryParameters.Add("type", imageType);

            return queryParameters;
        }

        public ServerImageDetails GetImage(string imageId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}", GetServiceEndpoint(identity, region), imageId));

            var response = ExecuteRESTRequest<GetImageDetailsResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null)
                return null;

            return response.Data.Image;
        }

        public bool DeleteImage(string imageId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}", GetServiceEndpoint(identity, region), imageId));

            var defaultSettings = BuildDefaultRequestSettings(new[] { 404 });
            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.DELETE, settings: defaultSettings);

            if (response == null || !_validServerActionResponseCode.Contains(response.StatusCode))
                return false; // throw new ExternalServiceException(response.StatusCode, response.Status, response.RawBody);

            return true;
        }

        public ServerImageDetails WaitForImageState(string imageId, string[] expectedStates, string[] errorStates, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            var details = GetImage(imageId, region, identity);

            int count = 0;
            while (!expectedStates.Contains(details.Status) && !errorStates.Contains(details.Status) && count < refreshCount)
            {
                Thread.Sleep(refreshDelayInMS);
                details = GetImage(imageId, region, identity);
                count++;
            }

            if (errorStates.Contains(details.Status))
                throw new ServerEnteredErrorStateException(details.Status);

            return details;
        }

        public ServerImageDetails WaitForImageState(string imageId, string expectedState, string[] errorStates, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            return WaitForImageState(imageId, new[] { expectedState }, errorStates, refreshCount, refreshDelayInMS, region, identity);
        }

        public ServerImageDetails WaitForImageActive(string imageId, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            return WaitForImageState(imageId, ImageState.ACTIVE, new[] { ImageState.ERROR, ImageState.UNKNOWN, ImageState.SUSPENDED }, refreshCount, refreshDelayInMS, region, identity);
        }

        public void WaitForImageDeleted(string imageId, int refreshCount = 600, int refreshDelayInMS = 2400, string region = null, CloudIdentity identity = null)
        {
            try
            {
                WaitForImageState(imageId, ImageState.DELETED,
                                  new[] {ImageState.ERROR, ImageState.UNKNOWN, ImageState.SUSPENDED},
                                  refreshCount, refreshDelayInMS, region, identity);
            }
            catch (net.openstack.Core.Exceptions.Response.ItemNotFoundException){} // there is the possibility that the image can be ACTIVE for one pass and then 
                                                                                   // by the next pass a 404 is returned.  This is due to the VERY limited window in which
                                                                                   // the image goes into the DELETED state before it is removed from the system.
        }

        #endregion

        #region Server Metadata

        public Metadata ListServerMetadata(string serverId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest<MetaDataResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null)
                return null;

            return response.Data.Metadata;
        }

        public bool SetServerMetadata(string serverId, Metadata metadata, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.PUT, new UpdateMetadataRequest { Metadata = metadata });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public bool UpdateServerMetadata(string serverId, Metadata metadata, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata", GetServiceEndpoint(identity, region), serverId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.POST, new UpdateMetadataRequest { Metadata = metadata });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public string GetServerMetadataItem(string serverId, string key, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata/{2}", GetServiceEndpoint(identity, region), serverId, key));

            var response = ExecuteRESTRequest<MetaDataResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || (response.StatusCode != 200 && response.StatusCode != 203) || response.Data == null || response.Data.Metadata == null || response.Data.Metadata.Count == 0)
                return null;

            return response.Data.Metadata.First().Value;
        }

        public bool SetServerMetadataItem(string serverId, string key, string value, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata/{2}", GetServiceEndpoint(identity, region), serverId, key));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.PUT, new UpdateMetadataItemRequest { Metadata = new Metadata {{key, value}} });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public bool DeleteServerMetadataItem(string serverId, string key, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/servers/{1}/metadata/{2}", GetServiceEndpoint(identity, region), serverId, key));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.DELETE);

            if (response.StatusCode == 204)
                return true;

            return false;
        }

        #endregion

        #region Image Metadata

        public Metadata ListImageMetadata(string imageId, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata", GetServiceEndpoint(identity, region), imageId));

            var response = ExecuteRESTRequest<MetaDataResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null)
                return null;

            return response.Data.Metadata;
        }

        public bool SetImageMetadata(string imageId, Metadata metadata, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata", GetServiceEndpoint(identity, region), imageId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.PUT, new UpdateMetadataRequest { Metadata = metadata });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public bool UpdateImageMetadata(string imageId, Metadata metadata, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata", GetServiceEndpoint(identity, region), imageId));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.POST, new UpdateMetadataRequest { Metadata = metadata });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public string GetImageMetadataItem(string imageId, string key, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata/{2}", GetServiceEndpoint(identity, region), imageId, key));

            var response = ExecuteRESTRequest<MetaDataResponse>(identity, urlPath, HttpMethod.GET);

            if (response == null || response.Data == null || response.Data.Metadata == null || response.Data.Metadata.Count == 0)
                return null;

            return response.Data.Metadata.First().Value;
        }

        public bool SetImageMetadataItem(string imageId, string key, string value, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata/{2}", GetServiceEndpoint(identity, region), imageId, key));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.PUT, new UpdateMetadataItemRequest { Metadata = new Metadata { { key, value } } });

            if (response.StatusCode == 200)
                return true;

            return false;
        }

        public bool DeleteImageMetadataItem(string imageId, string key, string region = null, CloudIdentity identity = null)
        {
            var urlPath = new Uri(string.Format("{0}/images/{1}/metadata/{2}", GetServiceEndpoint(identity, region), imageId, key));

            var response = ExecuteRESTRequest(identity, urlPath, HttpMethod.DELETE);

            if (response.StatusCode == 204)
                return true;

            return false;
        }

        #endregion

        #region Private methods
        
        protected string GetServiceEndpoint(CloudIdentity identity = null, string region = null)
        {
            return base.GetPublicServiceEndpoint(identity, "cloudServersOpenStack", region);
        }

        private ICloudServersProvider BuildProvider(CloudIdentity identity)
        {
            if (identity == null)
                identity = _defaultIdentity;

            return new CloudServersProvider(identity, _identityProvider, _restService);
        }

        #endregion
    }

    public class ServerEnteredErrorStateException : Exception
    {
        public string Status { get; private set; }

        public ServerEnteredErrorStateException(string status) : base(string.Format("The server entered an error state: '{0}'", status))
        {
            Status = status;
        }
    }
}