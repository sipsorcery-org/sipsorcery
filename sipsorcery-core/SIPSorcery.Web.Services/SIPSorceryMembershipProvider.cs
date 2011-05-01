using System;
using System.Collections.Generic;
using System.Configuration.Provider;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Web;
using System.Web.Security;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class SIPSorceryMembershipProvider : MembershipProvider
    {
        private ILog logger = AppState.logger;

        public override bool ValidateUser(string username, string password)
        {
            try
            {
                string ipAddress = null;

                if (OperationContext.Current != null)
                {
                    OperationContext context = OperationContext.Current;
                    MessageProperties properties = context.IncomingMessageProperties;
                    RemoteEndpointMessageProperty endpoint = properties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
                    if (endpoint != null)
                    {
                        ipAddress = endpoint.Address;
                    }
                }
                else if (HttpContext.Current != null)
                {
                    ipAddress = HttpContext.Current.Request.UserHostAddress;
                }

                if (username.IsNullOrBlank() || password.IsNullOrBlank())
                {
                    logger.Debug("Login from " + ipAddress + " failed, either username or password was not specified.");
                    return false;
                }
                else
                {
                    using (var sipSorceryEntities = new SIPSorceryEntities())
                    {
                        Customer customer = (from cust in sipSorceryEntities.Customers
                                             where cust.Name.ToLower() == username.ToLower()
                                             select cust).SingleOrDefault();

                        if (customer != null && customer.CustomerPassword == password)
                        {
                            if (!customer.EmailAddressConfirmed)
                            {
                                throw new ApplicationException("Your email address has not yet been confirmed.");
                            }
                            else if (customer.Suspended)
                            {
                                throw new ApplicationException("Your account is suspended.");
                            }
                            else
                            {
                                logger.Debug("Login from " + ipAddress + " successful for " + username + ".");
                                return true;
                            }
                        }
                        else
                        {
                            logger.Debug("Login from " + ipAddress + " failed for " + username + ".");
                            return false;
                        }
                    }
                }
            }
            catch (ApplicationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryMembershipProvider ValidateUser. " + excp.Message);
                throw new ApplicationException("There was an " + excp.GetType().ToString() + " server error attempting to login.");
            }
        }

        public override string ApplicationName
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            throw new NotImplementedException();
        }

        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status)
        {
            throw new NotImplementedException();
        }

        public override bool DeleteUser(string username, bool deleteAllRelatedData)
        {
            throw new NotImplementedException();
        }

        public override bool EnablePasswordReset
        {
            get { throw new NotImplementedException(); }
        }

        public override bool EnablePasswordRetrieval
        {
            get { throw new NotImplementedException(); }
        }

        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override int GetNumberOfUsersOnline()
        {
            throw new NotImplementedException();
        }

        public override string GetPassword(string username, string answer)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser GetUser(string username, bool userIsOnline)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline)
        {
            throw new NotImplementedException();
        }

        public override string GetUserNameByEmail(string email)
        {
            throw new NotImplementedException();
        }

        public override int MaxInvalidPasswordAttempts
        {
            get { throw new NotImplementedException(); }
        }

        public override int MinRequiredNonAlphanumericCharacters
        {
            get { throw new NotImplementedException(); }
        }

        public override int MinRequiredPasswordLength
        {
            get { throw new NotImplementedException(); }
        }

        public override int PasswordAttemptWindow
        {
            get { throw new NotImplementedException(); }
        }

        public override MembershipPasswordFormat PasswordFormat
        {
            get { throw new NotImplementedException(); }
        }

        public override string PasswordStrengthRegularExpression
        {
            get { throw new NotImplementedException(); }
        }

        public override bool RequiresQuestionAndAnswer
        {
            get { throw new NotImplementedException(); }
        }

        public override bool RequiresUniqueEmail
        {
            get { throw new NotImplementedException(); }
        }

        public override string ResetPassword(string username, string answer)
        {
            throw new NotImplementedException();
        }

        public override bool UnlockUser(string userName)
        {
            throw new NotImplementedException();
        }

        public override void UpdateUser(MembershipUser user)
        {
            throw new NotImplementedException();
        }
    }
}