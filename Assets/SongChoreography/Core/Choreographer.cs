using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SongChoreography
{
    public static class Choreographer
    {
        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static void ValidateAnalysis(MusicAnalysis a)
        {
            if (a == null || a.schemaVersion != 1 || !Finite(a.durationSeconds) || a.durationSeconds < 29.99 || a.durationSeconds > 60.01 || a.sampleRate <= 0)
                throw new InvalidDataException("音乐分析版本或片段时长无效。");
            if (a.beats == null || a.energy == null || a.energy.Count != a.beats.Count) throw new InvalidDataException("节拍与强弱数据不匹配。");
            double previous = -1;
            for (int i=0; i<a.beats.Count; i++)
            {
                if (!Finite(a.beats[i]) || a.beats[i] < 0 || a.beats[i] >= a.durationSeconds || a.beats[i] <= previous || !Finite(a.energy[i]) || a.energy[i] < 0 || a.energy[i] > 1)
                    throw new InvalidDataException("节拍必须按时间递增，强弱值应在 0～1 之间。");
                previous = a.beats[i];
            }
        }
        public static List<double> BeatTimes(MusicAnalysis a, GridSettings g)
        {
            ValidateAnalysis(a);
            if (!Finite(g.bpm) || g.bpm < 40 || g.bpm > 240 || !Finite(g.firstBeatSeconds) || g.firstBeatSeconds < 0 || g.firstBeatSeconds > 3 || g.eightCountOffset < 0 || g.eightCountOffset > 7)
                throw new ArgumentException("BPM 应为 40～240，首拍为 0～3 秒，八拍起始计数为 1～8。");
            if (!g.manual && a.beats.Count >= 8) return new List<double>(a.beats);
            if (!g.manual) throw new InvalidOperationException("请先手动校正节拍。");
            var result = new List<double>();
            for (int n=0; ;n++) { double t=g.firstBeatSeconds+n*60/g.bpm; if(t>=a.durationSeconds) break; result.Add(t); }
            return result;
        }
        public static List<MotionDefinition> Eligible(MotionCatalog c, string mode)
        {
            if(!DanceStyles.IsKnown(mode))throw new ArgumentException("不支持的舞种。");
            if (c == null || c.schemaVersion != 1 || c.motions == null) throw new InvalidDataException("动作库版本无效。");
            if (c.motions.Any(m=>m == null || string.IsNullOrEmpty(m.id)) || c.motions.Select(m=>m.id).Distinct().Count()!=c.motions.Count)
                throw new InvalidDataException("动作编号必须唯一且非空。");
            return c.motions.Where(m=>m.style==mode && (m.beats==4 || m.beats==8) && m.difficulty==1 && Finite(m.energy) && m.energy>=0 && m.energy<=1 && !string.IsNullOrEmpty(m.resource) && ValidSupport(m) &&
                (mode=="diagnostic" || (m.approved && !string.IsNullOrWhiteSpace(m.source) && !string.IsNullOrWhiteSpace(m.license) && !string.IsNullOrWhiteSpace(m.reviewer) && !string.IsNullOrWhiteSpace(m.reviewNotes)))).ToList();
        }
        static bool ValidSupport(MotionDefinition motion)=>Finite(motion.transitionBeats)&&motion.transitionBeats>.05f&&motion.transitionBeats<=1&&(!motion.groundToFloor||(motion.supportFps>=1&&motion.supportFps<=240&&motion.supportHeights!=null&&motion.supportHeights.Count>=2&&motion.supportHeights.Count<=14401&&motion.supportHeights.All(h=>Finite(h)&&h>=0&&h<=1)));
        public static bool CanFollow(MotionDefinition a, MotionDefinition b) => a.safeNext != null && a.safeNext.Contains(b.id);
        public static List<DanceSegment> Generate(DanceProject project, MotionCatalog catalog)
        {
            var motions=Eligible(catalog,project.mode);
            if (project.mode!="diagnostic" && motions.Count<8) throw new InvalidOperationException(DanceStyles.Name(project.mode)+" 素材缺口：需要至少 8 个有来源、许可、审核人与衔接审核记录的入门动作。");
            if (motions.Count==0) throw new InvalidOperationException("没有符合条件的动作素材。");
            var beats=BeatTimes(project.analysis,project.grid);
            var slots=new List<DanceSegment>();
            // Stable slots keep replacement and locks independent of motion choice.
            // A count other than one starts with a partial phrase; its animation
            // begins at that same count and reaches a reviewed cycle boundary.
            int slotBeats=DanceStyles.SlotBeats(project.mode);
            for(int i=0;i<beats.Count;)
            {
                int offset=i==0?project.grid.eightCountOffset:0, count=slotBeats-offset;
                bool complete=i+count<beats.Count;
                double start=beats[i], end=complete?beats[i+count]:project.analysis.durationSeconds;
                double period=i+1<beats.Count?beats[i+1]-start:60/project.grid.bpm;
                // Detected beat intervals can vary. A complete group still has
                // exactly count beats; only the final incomplete group is cut.
                slots.Add(new DanceSegment{startSeconds=start,endSeconds=end,startBeatOffset=offset,beatCount=complete?count:(float)Math.Min(count,(end-start)/period)});
                i+=count;
            }
            if(slots.Count==0) throw new InvalidOperationException("没有可编排的节拍。");
            foreach(var old in project.segments.Where(x=>x.locked))
            {
                var match=slots.FirstOrDefault(x=>Math.Abs(x.startSeconds-old.startSeconds)<1e-5 && Math.Abs(x.endSeconds-old.endSeconds)<1e-5 && Math.Abs(x.beatCount-old.beatCount)<1e-4 && x.startBeatOffset==old.startBeatOffset);
                if(match==null) throw new InvalidOperationException("节拍校正会改变已锁定片段，请先解除锁定。");
                match.motionId=old.motionId; match.locked=true;
            }
            int n=slots.Count, m=motions.Count;
            var cost=new double[n,m]; var parent=new int[n,m];
            for(int i=0;i<n;i++) for(int j=0;j<m;j++)
            {
                cost[i,j]=double.PositiveInfinity; parent[i,j]=-1;
                if(slots[i].locked && slots[i].motionId!=motions[j].id) continue;
                if(slots[i].startBeatOffset+slots[i].beatCount>motions[j].beats+.001 && !CanFollow(motions[j],motions[j]))continue;
                var energy=EnergyAt(project.analysis,(slots[i].startSeconds+slots[i].endSeconds)/2);
                double own=Math.Pow(energy-motions[j].energy,2)*3 + Noise(project.seed,i,motions[j].id)*.5;
                if(i==0) { cost[i,j]=own; continue; }
                for(int k=0;k<m;k++) if(CanFollow(motions[k],motions[j]))
                {
                    double score=cost[i-1,k]+own+(k==j?2:0);
                    if(score<cost[i,j]) { cost[i,j]=score;parent[i,j]=k; }
                }
            }
            int last=0;
            for(int j=1;j<m;j++) if(cost[n-1,j]<cost[n-1,last]) last=j;
            if(double.IsPositiveInfinity(cost[n-1,last])) throw new InvalidOperationException("已锁定动作与安全连接条件无法组成完整序列，请更换动作或解除锁定。");
            for(int i=n-1;i>=0;i--) { slots[i].motionId=motions[last].id;last=parent[i,last]; }
            return slots;
        }
        public static List<DanceSegment> GenerateAnother(DanceProject project,MotionCatalog catalog,out int chosenSeed)
        {
            int originalSeed=project.seed;chosenSeed=originalSeed;
            if(project.segments.Count>0&&project.segments.All(s=>s.locked))throw new InvalidOperationException("所有片段均已锁定，请先解锁需要更换的片段。");
            List<DanceSegment> best=null;int bestChanged=0,bestUnique=-1;
            try
            {
                // A new seed can still find the same optimum. Compare actual
                // sequences and select the most changed of 32 deterministic
                // candidates, always using the existing reviewed graph.
                for(int attempt=1;attempt<=32;attempt++)
                {
                    project.seed=unchecked(originalSeed+attempt);var candidate=Generate(project,catalog);
                    int changed=candidate.Where((s,i)=>!s.locked&&(i>=project.segments.Count||s.motionId!=project.segments[i].motionId)).Count();
                    int unique=candidate.Select(s=>s.motionId).Distinct().Count();
                    if(changed>bestChanged||(changed==bestChanged&&changed>0&&unique>bestUnique))
                    {best=candidate;bestChanged=changed;bestUnique=unique;chosenSeed=project.seed;}
                }
            }
            finally{project.seed=originalSeed;}
            if(best==null)throw new InvalidOperationException("在当前锁定和已审核连接下，暂未找到不同组合。请解锁更多片段或调整音乐片段。");
            return best;
        }
        static double Noise(int seed,int index,string id)
        {
            unchecked { uint h=(uint)seed ^ ((uint)index*16777619u);foreach(char c in id) h=(h^c)*16777619u;return (h%10000)/10000.0; }
        }
        public static double MotionCycles(DanceSegment segment,int motionBeats,double songTime)
        {
            double unit=Math.Max(0,Math.Min(1,(songTime-segment.startSeconds)/(segment.endSeconds-segment.startSeconds)));
            return (segment.startBeatOffset+unit*segment.beatCount)/motionBeats;
        }
        public static float EnergyAt(MusicAnalysis a,double t)
        {
            if(a.energy.Count==0) return .5f;
            int i=a.beats.BinarySearch(t);if(i<0)i=~i;return a.energy[Math.Min(i,a.energy.Count-1)];
        }
        public static List<MotionDefinition> Replacements(DanceProject p,MotionCatalog c,int index)
        {
            if(index<0||index>=p.segments.Count) return new List<MotionDefinition>();
            var eligible=Eligible(c,p.mode);
            return eligible.Where(m=>(p.segments[index].startBeatOffset+p.segments[index].beatCount<=m.beats+.001 || CanFollow(m,m)) && (index==0 || CanFollow(eligible.First(x=>x.id==p.segments[index-1].motionId),m)) &&
                (index==p.segments.Count-1 || CanFollow(m,eligible.First(x=>x.id==p.segments[index+1].motionId)))).ToList();
        }
        public static void Replace(DanceProject p,MotionCatalog c,int index,string id)
        {
            if(index<0||index>=p.segments.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if(p.segments[index].locked) throw new InvalidOperationException("请先解锁此片段再替换。");
            if(!Replacements(p,c,index).Any(m=>m.id==id)) throw new InvalidOperationException("此动作与前后片段没有审核过的安全连接。");
            p.segments[index].motionId=id;
        }
        public static void ValidateProject(DanceProject p,MotionCatalog c)
        {
            if(p==null||p.schemaVersion!=1||p.grid==null||p.segments==null || !DanceStyles.IsKnown(p.mode)) throw new InvalidDataException("项目版本或舞种无效。");
            if(p.segments.Count>0 || p.grid.manual)BeatTimes(p.analysis,p.grid);else ValidateAnalysis(p.analysis);
            if(!Finite(p.position)||p.position<0||p.position>p.analysis.durationSeconds || (p.speed!=1 && p.speed!=.75f && p.speed!=.5f) || !Finite(p.loopStart)||!Finite(p.loopEnd)||p.loopStart<0||p.loopEnd>p.analysis.durationSeconds||p.loopEnd<=p.loopStart||p.view<0||p.view>6)
                throw new InvalidDataException("播放设置无效。");
            if(p.camera==null)p.camera=new CameraSettings();
            var camera=p.camera;
            if(!Finite(camera.yaw)||!Finite(camera.pitch)||camera.pitch< -15||camera.pitch>65||!Finite(camera.distanceScale)||camera.distanceScale<.55f||camera.distanceScale>2.4f||!Finite(camera.panX)||Math.Abs(camera.panX)>1||!Finite(camera.panY)||Math.Abs(camera.panY)>1||!Finite(camera.fieldOfView)||camera.fieldOfView<30||camera.fieldOfView>60)
                throw new InvalidDataException("镜头参数超出可用范围。");
            if(string.IsNullOrEmpty(p.character))p.character="female";
            if(p.character!="female"&&p.character!="male")throw new InvalidDataException("角色类型无效。");
            var motions=Eligible(c,p.mode).ToDictionary(m=>m.id);
            double end=-1; MotionDefinition previous=null;
            foreach(var s in p.segments)
            {
                if(!Finite(s.startSeconds)||!Finite(s.endSeconds)||!Finite(s.beatCount)||s.beatCount<=0||s.beatCount>8.01||s.startSeconds<0||s.endSeconds>p.analysis.durationSeconds+.001||s.endSeconds<=s.startSeconds || (end>=0 && Math.Abs(s.startSeconds-end)>.001) || !motions.TryGetValue(s.motionId,out var motion))
                    throw new InvalidDataException("编舞片段的时间或动作引用无效。");
                if(s.startBeatOffset<0||s.startBeatOffset>7||(previous!=null&&s.startBeatOffset!=0)||(s.startBeatOffset>0&&s.startBeatOffset+s.beatCount>8.01))throw new InvalidDataException("八拍起始相位无效：只有首段可以从中间拍开始，并须在第八拍结束。");
                if(previous!=null&&!CanFollow(previous,motion)) throw new InvalidDataException("编舞包含未经审核的动作连接。");
                if(s.startBeatOffset+s.beatCount>motion.beats+.001 && !CanFollow(motion,motion))throw new InvalidDataException("动作循环未经衔接审核。");
                end=s.endSeconds;previous=motion;
            }
        }
        public static string ResolveLocal(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)) throw new InvalidDataException("项目音频路径必须是相对路径。");
            string full=Path.GetFullPath(Path.Combine(root,relative));
            if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("项目音频路径不能越出项目文件夹。");
            return full;
        }
    }
}
