namespace System.Data.Linq.Mapping
{
    /// <summary>
    /// Mock class. Usage of class in executed code will result in exception!
    /// </summary>
    public sealed class AttributeMappingSource : MappingSource
    {
        protected override MetaModel CreateModel(Type dataContextType)
        {
            throw new NotImplementedException();
        }
    }
}
