using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UniGLTF;


namespace VrmLib
{
    /// <summary>
    /// ひと塊のアニメーション。
    /// UnityのAnimationClip, Gltfのanimationに相当する
    /// </summary>
    public class Animation : GltfId
    {
        public string Name;

        public readonly Dictionary<Node, NodeAnimation> NodeMap = new Dictionary<Node, NodeAnimation>();

        TimeSpan m_lastTime;
        public TimeSpan Duration => m_lastTime;

        int m_channels = 0;

        public Animation(string name)
        {
            Name = name;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Name);
            sb.Append($" {m_channels}channels {m_lastTime}sec");
            return sb.ToString();
        }

        public NodeAnimation GetOrCreateNodeAnimation(Node node)
        {
            NodeAnimation nodeAnimation;
            if (!NodeMap.TryGetValue(node, out nodeAnimation))
            {
                nodeAnimation = new NodeAnimation();
                NodeMap.Add(node, nodeAnimation);
            }
            return nodeAnimation;
        }

        public void AddCurve(Node node, NodeAnimation nodeAnimation)
        {
            NodeMap.Add(node, nodeAnimation);
            if (nodeAnimation.Duration > m_lastTime)
            {
                m_lastTime = nodeAnimation.Duration;
            }
        }

        public void UpdateChannelsAndLastTime()
        {
            var lastTime = 0.0f;
            m_channels = 0;
            foreach (var animation in NodeMap.Values)
            {
                foreach (var curve in animation.Curves.Values)
                {
                    lastTime = Math.Max(lastTime, curve.LastTime);
                    ++m_channels;
                }
            }

            var duration = TimeSpan.FromSeconds(lastTime);
            if (duration > m_lastTime)
            {
                m_lastTime = duration;
            }
        }

        public void SetTime(TimeSpan elapsed)
        {
            // repeat
            while (m_lastTime > TimeSpan.Zero && elapsed > m_lastTime)
            {
                elapsed -= m_lastTime;
            }

            foreach (var (node, animation) in NodeMap.Select(kv => (kv.Key, kv.Value)))
            {
                foreach (var (target, curve) in animation.Curves.Select(kv => (kv.Key, kv.Value)))
                {
                    switch (target)
                    {
                        case AnimationPathType.Translation:
                            node.LocalTranslationWithoutUpdate = curve.GetVector3(elapsed);
                            break;

                        case AnimationPathType.Rotation:
                            node.LocalRotationWithoutUpdate = curve.GetQuaternion(elapsed);
                            break;

                        case AnimationPathType.Scale:
                            node.LocalScalingWithoutUpdate = curve.GetVector3(elapsed);
                            break;

                        case AnimationPathType.Weights:
                            // TODO: morph target
                            break;

                        default:
                            throw new NotImplementedException();
                    }
                }
            }
        }

        /// <summary>
        /// ノードの参照するカーブとフレーム位置を指し示す
        /// </summary>
        struct KeyFrameReference
        {
            public Node Node => KV.Key;
            public NodeAnimation NodeAnimation => KV.Value;

            public CurveSampler Rotation => NodeAnimation.Curves[AnimationPathType.Rotation];

            public readonly KeyValuePair<Node, NodeAnimation> KV;
            public readonly int Index;

            public float Seconds
            {
                get
                {
                    var span = Rotation.In.Bytes.Reinterpret<Single>(1);
                    if (Index < span.Length)
                    {
                        return span[Index];
                    }
                    return float.NaN;
                }
            }

            public int Count => Rotation.In.Count;

            public KeyFrameReference(KeyValuePair<Node, NodeAnimation> kv, int index)
            {
                KV = kv;
                Index = index;
            }
        }

        /// <summary>
        /// 同じ時間のキーフレームをまとめて列挙する
        /// </summary>
        IEnumerable<(TimeSpan, IReadOnlyList<KeyFrameReference>)> KeyFramesGroupBySeconds()
        {
            Dictionary<KeyValuePair<Node, NodeAnimation>, int> curves = this.NodeMap.ToDictionary(kv => kv, kv => 0);

            /// すべてのキーフレームを消費するまでループする
            var list = new List<KeyFrameReference>();
            while (curves.Any())
            {
                list.Clear();

                var min = float.PositiveInfinity;
                foreach (var kv in curves)
                {
                    var curve = kv.Key;
                    var index = kv.Value;
                    var keyframe = new KeyFrameReference(curve, index);
                    var seconds = keyframe.Seconds;
                    if (seconds < min)
                    {
                        // 各カーブの先頭のキーフレームのうち時間が最小のものを得る
                        min = seconds;
                        list.Clear();
                        list.Add(keyframe);
                    }
                    else if (seconds == min)
                    {
                        // 同じ時間の場合はリストに詰める
                        list.Add(keyframe);
                    }
                }

                // 最小時間のキーフレームを列挙する
                yield return (TimeSpan.FromSeconds(min), list);

                // 最小時間として列挙したキーフレームを消費する
                foreach (var keyframe in list)
                {
                    if (keyframe.Index + 1 < keyframe.Count)
                    {
                        // next
                        curves[keyframe.KV]++;
                    }
                    else
                    {
                        // remove
                        curves.Remove(keyframe.KV);
                    }
                }
            }
        }

        Node m_root;
        Node Root
        {
            get
            {
                if (m_root == null)
                {
                    var keys = NodeMap.Keys;
                    foreach (var key in keys)
                    {
                        if (!key.Ancestors().Intersect(keys).Any())
                        {
                            m_root = key;
                            break;
                        }
                    }
                }
                return m_root;
            }
        }
    }
}
