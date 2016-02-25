using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace SIPSorcery.SIP.App
{
    public delegate void SetterDelegate(object instance, string propertyName, object value);

    public delegate void SIPAssetDelegate<T>(T asset);
    public delegate void SIPAssetsModifiedDelegate();
    public delegate void SIPAssetReloadedDelegate();
    public delegate T SIPAssetGetByIdDelegate<T>(Guid id);
    public delegate object SIPAssetGetPropertyByIdDelegate<T>(Guid id, string propertyName);
    public delegate T SIPAssetGetDelegate<T>(Expression<Func<T, bool>> where);
    public delegate int SIPAssetCountDelegate<T>(Expression<Func<T, bool>> where);
    public delegate List<T> SIPAssetGetListDelegate<T>(Expression<Func<T, bool>> where, string orderByField, int offset, int limit);
    public delegate T SIPAssetUpdateDelegate<T>(T asset);
    public delegate void SIPAssetUpdatePropertyDelegate<T>(Guid id, string propertyName, object value);
    public delegate void SIPAssetDeleteDelegate<T>(T asset);
    public delegate void SIPAssetDeleteBatchDelegate<T>(Expression<Func<T, bool>> where);

    // Real-time call control delegates.
    public delegate RtccCustomerAccount RtccGetCustomerDelegate(string owner, string accountCode);
    public delegate RtccRate RtccGetRateDelegate(string owner, string rateCode, string rateDestination, string ratePlan);
    public delegate decimal RtccGetBalanceDelegate(string accountCode);
    public delegate decimal RtccReserveInitialCreditDelegate(string accountCode, RtccRate rate, SIPCDR cdr, out int intialSeconds);
    public delegate void RtccUpdateCdrDelegate(string cdrID, SIPCDR cdr);

    public class RtccCustomerAccount
    {
        public string AccountCode;
        public string RatePlan;
    }

    public class RtccRate
    {
        public decimal RatePerIncrement;
        public decimal SetupCost;
    }
}
