using System;
using UnityEngine;

namespace SongChoreography
{
    public static class DancerCharacters
    {
        public static GameObject Create(string id)
        {
            if(id!="female"&&id!="male")throw new ArgumentException("未知角色。");
            var prefab=Resources.Load<GameObject>("Characters/"+id);
            if(!prefab)throw new InvalidOperationException("完整人形角色未准备，请先运行角色准备与 Unity 构建步骤。");
            var instance=UnityEngine.Object.Instantiate(prefab);instance.name="Dancer / "+id;
            var animator=instance.GetComponentInChildren<Animator>();
            if(!animator||!animator.avatar||!animator.avatar.isHuman||!animator.avatar.isValid)throw new InvalidOperationException("角色 Humanoid 配置无效。");
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.updateWhenOffscreen=true;
            return instance;
        }
    }
}
