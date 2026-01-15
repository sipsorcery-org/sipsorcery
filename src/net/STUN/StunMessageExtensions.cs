using SIPSorcery.Net;

namespace SIPSorcery.Net;

internal static class StunMessageExtensions
{
    public static STUNAttribute? FirstOrDefaultAttribute(this STUNMessage stunMessage, STUNAttributeTypesEnum attributeType)
    {
        foreach (var attribute in stunMessage.Attributes)
        {
            if (attribute.AttributeType == attributeType)
            {
                return attribute;
            }
        }

        return null;
    }

    public static TAttribute? FirstOrDefaultAttribute<TAttribute>(this STUNMessage stunMessage, STUNAttributeTypesEnum attributeType)
        where TAttribute : STUNAttribute
    {
        return stunMessage.FirstOrDefaultAttribute(attributeType) as TAttribute;
    }
}
