using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NHibernate;
using NHibernate.Cfg;

namespace SIPSorcery.Sys
{
    public class NHibernateHelper
    {
        private static ISessionFactory _sessionFactory; 
        private static ISessionFactory SessionFactory 
        { 
            get { 
                if (_sessionFactory == null) 
                { 
                    var configuration = new Configuration(); 
                    configuration.Configure();
                    _sessionFactory = configuration.BuildSessionFactory();
                } 
                return _sessionFactory; 
            } 
        }         
        
        public static ISession OpenSession() 
        { 
            return SessionFactory.OpenSession(); 
        }
    }
}
