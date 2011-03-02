
namespace SIPSorcery.SIP.App.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.ServiceModel.DomainServices.Hosting;
    using System.ServiceModel.DomainServices.Server;


    // The MetadataTypeAttribute identifies SIPDialplanOptionMetadata as the class
    // that carries additional metadata for the SIPDialplanOption class.
    [MetadataTypeAttribute(typeof(SIPDialplanOption.SIPDialplanOptionMetadata))]
    public partial class SIPDialplanOption
    {

        // This class allows you to attach custom attributes to properties
        // of the SIPDialplanOption class.
        //
        // For example, the following marks the Xyz property as a
        // required property and specifies the format for valid values:
        //    [Required]
        //    [RegularExpression("[A-Z][A-Za-z0-9]*")]
        //    [StringLength(32)]
        //    public string Xyz { get; set; }
        internal sealed class SIPDialplanOptionMetadata
        {

            // Metadata classes are not meant to be instantiated.
            private SIPDialplanOptionMetadata()
            {
            }

            public string allowedcountrycodes { get; set; }

            public Nullable<int> areacode { get; set; }

            public Nullable<int> countrycode { get; set; }

            public string dialplanname { get; set; }

            public bool enablesafeguards { get; set; }

            public string enumservers { get; set; }

            public string excludedprefixes { get; set; }

            public string id { get; set; }

            public string owner { get; set; }

            public string timezone { get; set; }

            public string whitepageskey { get; set; }
        }
    }

    // The MetadataTypeAttribute identifies SIPDialplanProviderMetadata as the class
    // that carries additional metadata for the SIPDialplanProvider class.
    [MetadataTypeAttribute(typeof(SIPDialplanProvider.SIPDialplanProviderMetadata))]
    public partial class SIPDialplanProvider
    {

        // This class allows you to attach custom attributes to properties
        // of the SIPDialplanProvider class.
        //
        // For example, the following marks the Xyz property as a
        // required property and specifies the format for valid values:
        //    [Required]
        //    [RegularExpression("[A-Z][A-Za-z0-9]*")]
        //    [StringLength(32)]
        //    public string Xyz { get; set; }
        internal sealed class SIPDialplanProviderMetadata
        {

            // Metadata classes are not meant to be instantiated.
            private SIPDialplanProviderMetadata()
            {
            }

            public string dialplanname { get; set; }

            public string id { get; set; }

            public string owner { get; set; }

            public string providerdescription { get; set; }

            public string providerdialstring { get; set; }

            public string providername { get; set; }

            public string providerprefix { get; set; }
        }
    }

    // The MetadataTypeAttribute identifies SIPDialplanRouteMetadata as the class
    // that carries additional metadata for the SIPDialplanRoute class.
    [MetadataTypeAttribute(typeof(SIPDialplanRoute.SIPDialplanRouteMetadata))]
    public partial class SIPDialplanRoute
    {

        // This class allows you to attach custom attributes to properties
        // of the SIPDialplanRoute class.
        //
        // For example, the following marks the Xyz property as a
        // required property and specifies the format for valid values:
        //    [Required]
        //    [RegularExpression("[A-Z][A-Za-z0-9]*")]
        //    [StringLength(32)]
        //    public string Xyz { get; set; }
        internal sealed class SIPDialplanRouteMetadata
        {

            // Metadata classes are not meant to be instantiated.
            private SIPDialplanRouteMetadata()
            {
            }

            public string dialplanname { get; set; }

            public string id { get; set; }

            public string owner { get; set; }

            public string routedescription { get; set; }

            public string routedestination { get; set; }

            public string routename { get; set; }

            public string routepattern { get; set; }

            public string routeprefix { get; set; }
        }
    }
}
