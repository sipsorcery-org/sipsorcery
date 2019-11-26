//-----------------------------------------------------------------------------
// Filename: VideoSubTypes.cs
//
// Description: Translation class for the Windows Media Foundation video sub type GUIDs.
// http://msdn.microsoft.com/en-us/library/windows/desktop/aa370819(v=vs.85).aspx
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 02 Jan 2015	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;

namespace SIPSorcery.SoftPhone
{
    public static class MFVideoSubTypes
    {
        public static Guid GetSubTypeGuid(this MFVideoSubTypesEnum e)
        {
            Type type = e.GetType();

            MemberInfo[] memInfo = type.GetMember(e.ToString());

            if (memInfo != null && memInfo.Length > 0)
            {
                object[] attrs = memInfo[0].GetCustomAttributes(typeof(VideoSubTypeAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    return ((VideoSubTypeAttribute)attrs[0]).SubTypeGuid;
                }
            }

            throw new ArgumentException("Enum " + e.ToString() + " has no VideoSubTypeAttribute defined.");
        }

        public static string GetSubTypeDescription(this MFVideoSubTypesEnum e)
        {
            Type type = e.GetType();

            MemberInfo[] memInfo = type.GetMember(e.ToString());

            if (memInfo != null && memInfo.Length > 0)
            {
                object[] attrs = memInfo[0].GetCustomAttributes(typeof(VideoSubTypeAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    return ((VideoSubTypeAttribute)attrs[0]).Description;
                }
            }

            throw new ArgumentException("Enum " + e.ToString() + " has no VideoSubTypeAttribute defined.");
        }

        public static MFVideoSubTypesEnum FindVideoSubTypeForGuid(Guid videoSubTypeGuid)
        {
            if(videoSubTypeGuid == MFVideoSubTypesEnum.MFVideoFormat_RGB24.GetSubTypeGuid()) return MFVideoSubTypesEnum.MFVideoFormat_RGB24;
            if (videoSubTypeGuid == MFVideoSubTypesEnum.MFVideoFormat_I420.GetSubTypeGuid()) return MFVideoSubTypesEnum.MFVideoFormat_I420;
            if (videoSubTypeGuid == MFVideoSubTypesEnum.MFVideoFormat_YUY2.GetSubTypeGuid()) return MFVideoSubTypesEnum.MFVideoFormat_YUY2;
            return MFVideoSubTypesEnum.Unknown;
        }
    }

    // http://msdn.microsoft.com/en-us/library/windows/desktop/dd757532(v=vs.85).aspx
    public enum MFVideoSubTypesEnum
    {
        Unknown,
        [VideoSubTypeAttribute("00000014-0000-0010-8000-00AA00389B71", "RGB24")]
        MFVideoFormat_RGB24,
        [VideoSubTypeAttribute("30323449-0000-0010-8000-00AA00389B71", "I420")]
        MFVideoFormat_I420,
        [VideoSubTypeAttribute("32595559-0000-0010-8000-00AA00389B71", "YUY2")]
        MFVideoFormat_YUY2,
    }

    public class VideoSubTypeAttribute : Attribute
    {
        public Guid SubTypeGuid { get; private set; }
        public string Description { get; private set; }

        internal VideoSubTypeAttribute(string subTypeGuid, string description)
        {
            this.SubTypeGuid = new Guid(subTypeGuid);
            this.Description = description;
        }
    }
}
