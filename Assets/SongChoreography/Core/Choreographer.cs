using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SongChoreography
{
    public static class Choreographer
    {
        public static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        public static string ResolveLocal(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative))throw new InvalidDataException("需要项目内相对路径。");
            string full=Path.GetFullPath(Path.Combine(root,relative));
            if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("路径超出项目目录。");
            return full;
        }
        public static void ValidateAnalysis(MusicAnalysis a)
        {
            if(a==null||a.schemaVersion!=1||!Finite(a.durationSeconds)||a.durationSeconds<29.99||a.durationSeconds>60.01||a.sampleRate<=0||!Finite(a.bpm)||a.bpm<40||a.bpm>240)throw new InvalidDataException("音乐分析无效。");
            if(a.beats==null||a.energy==null||a.beats.Count!=a.energy.Count)throw new InvalidDataException("节拍与强弱数量不一致。");
            double last=-1;
            for(int i=0;i<a.beats.Count;i++)
            {if(!Finite(a.beats[i])||a.beats[i]<=last||a.beats[i]<0||a.beats[i]>=a.durationSeconds||!Finite(a.energy[i])||a.energy[i]<0||a.energy[i]>1)throw new InvalidDataException("节拍或强弱值无效。");last=a.beats[i];}
            foreach(string path in new[]{a.normalAudio,a.slow075Audio,a.slow050Audio})ResolveLocal(Path.GetTempPath(),path);
        }
        public static void ValidateCatalog(AssetCatalog c)
        {
            if(c==null||c.schemaVersion!=2||c.characters==null||c.motions==null||c.transitions==null)throw new InvalidDataException("生成素材库版本错误。");
            if(c.characters.Any(a=>a==null||string.IsNullOrWhiteSpace(a.id)||string.IsNullOrWhiteSpace(a.signature))||c.characters.Select(a=>a.id).Distinct().Count()!=c.characters.Count)throw new InvalidDataException("人物编号或签名无效。");
            if(c.motions.Any(m=>m==null||string.IsNullOrWhiteSpace(m.id)||string.IsNullOrWhiteSpace(m.signature))||c.motions.Select(m=>m.id).Distinct().Count()!=c.motions.Count)throw new InvalidDataException("动作编号或签名无效。");
            foreach(var m in c.motions)
            {
                if((m.beats!=4&&m.beats!=8)||!Finite(m.sourceBpm)||m.sourceBpm<60||m.sourceBpm>180||!Finite(m.energy)||m.energy<0||m.energy>1||!Finite(m.clipStart)||!Finite(m.clipEnd)||m.clipStart<0||m.clipEnd<=m.clipStart||m.reviewedCharacters==null||m.reviewedCharacters.Any(id=>!c.characters.Any(a=>a.id==id)))throw new InvalidDataException("动作元数据无效："+m.id);
                if(m.beatTimes==null||m.beatTimes.Count!=m.beats+1||Math.Abs(m.beatTimes[0]-m.clipStart)>.001||Math.Abs(m.beatTimes[m.beats]-m.clipEnd)>.001)throw new InvalidDataException("动作拍点未覆盖完整片段："+m.id);
                for(int i=0;i<m.beatTimes.Count;i++)if(!Finite(m.beatTimes[i])||(i>0&&m.beatTimes[i]<=m.beatTimes[i-1]))throw new InvalidDataException("动作拍点必须递增。");
            }
            foreach(var edge in c.transitions)
                if(edge==null||!Finite(edge.blendBeats)||edge.blendBeats<=0||edge.blendBeats>1||!c.characters.Any(a=>a.id==edge.characterId)||!c.motions.Any(m=>m.id==edge.from&&m.reviewedCharacters.Contains(edge.characterId))||!c.motions.Any(m=>m.id==edge.to&&m.reviewedCharacters.Contains(edge.characterId)))throw new InvalidDataException("连接引用了未复查的动作。");
        }
        public static List<MotionDefinition> Eligible(AssetCatalog c,string characterId,IEnumerable<string> enabledMotionIds=null)
        {
            ValidateCatalog(c);var enabled=enabledMotionIds==null?null:new HashSet<string>(enabledMotionIds,StringComparer.Ordinal);
            return c.motions.Where(m=>m.reviewedCharacters.Contains(characterId)&&(enabled==null||enabled.Contains(m.id))).ToList();
        }
        public static TransitionDefinition Edge(AssetCatalog c,string characterId,string from,string to)=>c.transitions.FirstOrDefault(e=>e.characterId==characterId&&e.from==from&&e.to==to);
        public static List<double> BeatTimes(MusicAnalysis a,GridSettings grid)
        {
            ValidateAnalysis(a);
            if(grid==null||!Finite(grid.bpm)||grid.bpm<40||grid.bpm>240||!Finite(grid.firstBeatSeconds)||grid.firstBeatSeconds<0||grid.firstBeatSeconds>=a.durationSeconds||grid.eightCountOffset<0||grid.eightCountOffset>7)throw new InvalidDataException("请校正 BPM、首拍和八拍起点。");
            if(!grid.manual)
            {if(a.beats.Count<8)throw new InvalidOperationException("自动节拍不足，请手动校正。");return new List<double>(a.beats);}
            var beats=new List<double>();
            for(int n=0;;n++){double t=grid.firstBeatSeconds+n*60/grid.bpm;if(t>=a.durationSeconds)break;beats.Add(t);}
            return beats;
        }
        public static List<DanceSegment> Slots(DanceProject p)
        {
            var beats=BeatTimes(p.analysis,p.grid);var result=new List<DanceSegment>();
            for(int i=0;i<beats.Count;)
            {
                int offset=i==0?p.grid.eightCountOffset:0,count=8-offset;
                double period=i+1<beats.Count?beats[i+1]-beats[i]:60/p.grid.bpm;
                bool complete=i+count<beats.Count;
                double end=complete?beats[i+count]:p.analysis.durationSeconds;
                result.Add(new DanceSegment{startSeconds=beats[i],endSeconds=end,startBeatOffset=offset,beatCount=complete?count:Math.Min(count,(end-beats[i])/period)});
                i+=count;
            }
            if(result.Count==0)throw new InvalidOperationException("所选片段没有可编排的节拍。");
            return result;
        }
        public static float EnergyAt(MusicAnalysis a,double t)
        {if(a.energy.Count==0)return .5f;int i=a.beats.BinarySearch(t);if(i<0)i=~i;return a.energy[Math.Min(i,a.energy.Count-1)];}
        sealed class Candidate {public double cost;public List<int> path=new List<int>();}
        static double Noise(int seed,int slot,string id)
        {unchecked{uint h=(uint)seed^(uint)slot*16777619u;foreach(char c in id)h=(h^c)*16777619u;return h%10000/10000.0;}}
        public static List<DanceSegment> Generate(DanceProject p,AssetCatalog catalog,IEnumerable<string> enabledMotionIds=null)
        {
            var motions=Eligible(catalog,p.characterId,enabledMotionIds).Where(m=>m.beats==8).OrderBy(m=>m.id,StringComparer.Ordinal).ToList();
            if(motions.Count==0)throw new InvalidOperationException("所选动作中没有此人物复查通过的八拍动作，请在动作库设置中选择。");
            var slots=Slots(p);
            foreach(var locked in p.segments.Where(s=>s.locked))
            {
                var slot=slots.FirstOrDefault(s=>Math.Abs(s.startSeconds-locked.startSeconds)<1e-6&&Math.Abs(s.endSeconds-locked.endSeconds)<1e-6&&s.startBeatOffset==locked.startBeatOffset&&Math.Abs(s.beatCount-locked.beatCount)<1e-6);
                if(slot==null)throw new InvalidOperationException("节拍改变会移动锁定片段，请先解锁。");
                slot.locked=true;slot.motionId=locked.motionId;
            }
            // Exact reachability first: locks cannot be lost to beam pruning.
            var reachable=new bool[slots.Count,motions.Count];
            for(int i=slots.Count-1;i>=0;i--)for(int j=0;j<motions.Count;j++)
            {
                if(slots[i].locked&&slots[i].motionId!=motions[j].id)continue;
                reachable[i,j]=i==slots.Count-1||Enumerable.Range(0,motions.Count).Any(k=>reachable[i+1,k]&&Edge(catalog,p.characterId,motions[j].id,motions[k].id)!=null);
            }
            var candidates=new List<Candidate>{new Candidate()};
            for(int i=0;i<slots.Count;i++)
            {
                var next=new List<Candidate>();float energy=EnergyAt(p.analysis,(slots[i].startSeconds+slots[i].endSeconds)/2);
                foreach(var candidate in candidates)for(int j=0;j<motions.Count;j++)
                {
                    if(!reachable[i,j]||(i>0&&Edge(catalog,p.characterId,motions[candidate.path.Last()].id,motions[j].id)==null))continue;
                    double score=candidate.cost+3*Math.Pow(energy-motions[j].energy,2)+Noise(p.seed,i,motions[j].id)*.35;
                    // Prefer covering the reviewed repertoire across the whole
                    // song. A short recency window alone permits small cycles
                    // to keep winning even when new reachable phrases exist.
                    score+=3*candidate.path.Count(previous=>previous==j);
                    for(int back=1;back<=Math.Min(4,candidate.path.Count);back++)if(candidate.path[candidate.path.Count-back]==j)score+=1.5/back;
                    var item=new Candidate{cost=score,path=new List<int>(candidate.path)};item.path.Add(j);next.Add(item);
                }
                candidates=next.OrderBy(c=>c.cost).Take(64).ToList();
                if(candidates.Count==0)throw new InvalidOperationException("已复查的连接无法组成此组合；请增加连接或解除锁定。原编舞未更改。");
            }
            for(int i=0;i<slots.Count;i++){var m=motions[candidates[0].path[i]];slots[i].motionId=m.id;slots[i].motionSignature=m.signature;}
            return slots;
        }
        public static double MotionTime(MotionDefinition m,DanceSegment s,double songTime)
        {
            double unit=Math.Max(0,Math.Min(1,(songTime-s.startSeconds)/(s.endSeconds-s.startSeconds)));
            double beat=Math.Min(m.beats,s.startBeatOffset+unit*s.beatCount);int lower=Math.Min(m.beats-1,(int)beat);
            return m.beatTimes[lower]+(m.beatTimes[lower+1]-m.beatTimes[lower])*(beat-lower);
        }
        public static void ValidateProject(DanceProject p,AssetCatalog c)
        {
            ValidateCatalog(c);
            if(p==null||p.schemaVersion!=2||p.segments==null||p.camera==null)throw new InvalidDataException("需要新版编舞项目；旧版项目保留在恢复快照中。");
            BeatTimes(p.analysis,p.grid);
            var character=c.characters.FirstOrDefault(a=>a.id==p.characterId&&a.signature==p.characterSignature);
            if(character==null)throw new InvalidDataException("项目人物不存在或已更新，请重新编排。");
            if(!Finite(p.position)||p.position<0||p.position>p.analysis.durationSeconds||!Finite(p.loopStart)||!Finite(p.loopEnd)||p.loopStart<0||p.loopEnd<=p.loopStart||p.loopEnd>p.analysis.durationSeconds||(p.speed!=1&&p.speed!=.75f&&p.speed!=.5f)||p.view<0||p.view>6)throw new InvalidDataException("播放参数无效。");
            var cam=p.camera;
            if(!Finite(cam.yaw)||!Finite(cam.pitch)||cam.pitch< -15||cam.pitch>65||!Finite(cam.distanceScale)||cam.distanceScale<.55f||cam.distanceScale>2.4f||!Finite(cam.panX)||Math.Abs(cam.panX)>1||!Finite(cam.panY)||Math.Abs(cam.panY)>1||!Finite(cam.fieldOfView)||cam.fieldOfView<30||cam.fieldOfView>60)throw new InvalidDataException("镜头参数无效。");
            MotionDefinition previous=null;double end=-1;
            foreach(var s in p.segments)
            {
                var m=c.motions.FirstOrDefault(a=>a.id==s.motionId&&a.signature==s.motionSignature&&a.reviewedCharacters.Contains(p.characterId));
                if(m==null||!Finite(s.startSeconds)||!Finite(s.endSeconds)||s.startSeconds<0||s.endSeconds>safeduration(p)||s.endSeconds<=s.startSeconds||!Finite(s.beatCount)||s.beatCount<=0||s.startBeatOffset<0||s.startBeatOffset+s.beatCount>m.beats+.001||(end>=0&&Math.Abs(s.startSeconds-end)>.001)||(previous!=null&&s.startBeatOffset!=0))throw new InvalidDataException("项目包含失效动作或错误时槽。");
                if(previous!=null&&Edge(c,p.characterId,previous.id,m.id)==null)throw new InvalidDataException("项目包含未经复查的连接。");
                previous=m;end=s.endSeconds;
            }
        }
        static double safeduration(DanceProject p)=>p.analysis.durationSeconds+.001;
    }
}
