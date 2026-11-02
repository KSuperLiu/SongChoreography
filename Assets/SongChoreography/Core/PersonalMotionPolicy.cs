using System;
using System.IO;
using System.Linq;

namespace SongChoreography
{
    public static class PersonalMotionPolicy
    {
        public static void AssertPublic(MotionCatalog catalog)
        {
            if(catalog==null||catalog.personalUseOnly||catalog.motions.Any(m=>m.personalUseOnly||m.localRigMotion||m.id.StartsWith("personal-",StringComparison.Ordinal)))
                throw new InvalidDataException("此构建包含仅供个人学习的动作，不能公开发布。");
        }
        public static MotionCatalog Merge(MotionCatalog shared,MotionCatalog personal)
        {
            if(shared==null||shared.schemaVersion!=1)throw new InvalidDataException("公共动作库版本无效。");
            if(personal==null)return shared;
            if(personal.schemaVersion!=1||!personal.personalUseOnly||string.IsNullOrWhiteSpace(personal.sourceNotice)||personal.motions==null)
                throw new InvalidDataException("个人动作库必须包含个人使用限制及来源说明。");
            if(personal.motions.Any(m=>m==null||!m.personalUseOnly||!m.id.StartsWith("personal-",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(m.source)||string.IsNullOrWhiteSpace(m.license)))
                throw new InvalidDataException("个人动作缺少来源、许可或个人使用标记。");
            var result=new MotionCatalog{personalUseOnly=true,sourceNotice=personal.sourceNotice,motions=shared.motions.Concat(personal.motions).ToList()};
            if(result.motions.Select(m=>m.id).Distinct(StringComparer.Ordinal).Count()!=result.motions.Count)
                throw new InvalidDataException("个人动作与已有动作 ID 重复。");
            var ids=personal.motions.Select(m=>m.id).ToArray();
            if(personal.motions.Any(m=>m.safeNext.Any(id=>!ids.Contains(id))))
                throw new InvalidDataException("个人动作的连接必须留在同一个个人动作库内。");
            if(personal.motions.Any(m=>m.approved&&!string.IsNullOrEmpty(m.sourceNext)&&!m.safeNext.Contains(m.sourceNext)))
                throw new InvalidDataException("连续源片段也必须经过动作连接复查。");
            return result;
        }
    }
}
