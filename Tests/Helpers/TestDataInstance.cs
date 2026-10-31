using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Replication;

namespace Tests.Helpers
{
    public class TestDataInstance : DataInstance
    {
        public bool IsDown { get; set; }

        public TestDataInstance(string name, DbContextOptions<EducationDbContext> options)
            : base(name, options) { }

        protected override bool CanConnect(EducationDbContext context)
        {
            return !IsDown && base.CanConnect(context);
        }
    }
}
