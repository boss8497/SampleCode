using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public class CharacterAttackInfo {
    [Flags]
    public enum AttackTriggerType {
        Default          = 1 << 0, // 밀리어택 기반
        Range            = 1 << 1, // 범위안에 있을때만 공격
        HPRate           = 1 << 2, // min ~ max 사이 Count만큼 사용
        AttackRate       = 1 << 3, // 기본 어택 횟수에따라 MaxAttackCount만큼 사용
        GlobalCoolTime   = 1 << 4, // 공격 후 딜레이(후딜)
        CharacterLevel   = 1 << 5, // 사용자 레벨
        FirstAttackDelay = 1 << 6, // 처음 공격 딜레이
    }
    
    public struct Range {
        public static Range One => new Range { min = 0, max = 1 };
        public        float min;
        public        float max;
    }

    public bool show = false;
    public string guid = System.Guid.NewGuid().ToString();
    
    
    public AttackTriggerType type = AttackTriggerType.Default;
    public int priority = 0;
    public float coolTime = 0;

    [ShowIf("@this.type.HasFlag(AttackTriggerType.HPRate)")]
    public float hpRateMin = 0;
    [ShowIf("@this.type.HasFlag(AttackTriggerType.HPRate) && this.must == false")]
    public float hpRateMax = 0;
    [ShowIf("@this.type.HasFlag(AttackTriggerType.HPRate)")]
    public bool must = false;

    [ShowIf("@this.type.HasFlag(AttackTriggerType.CharacterLevel)")]
    public int minLevel;

    [ShowIf("@this.type.HasFlag(AttackTriggerType.CharacterLevel)")]
    public int maxLevel;
    
    [ShowIf("@this.type.HasFlag(AttackTriggerType.GlobalCoolTime)")]
    public float globalCoolTime = 0;
    
    [ShowIf("@this.type.HasFlag(AttackTriggerType.FirstAttackDelay)")]
    public float firstAttackCoolTime = 0;

    private string _globalCoolTimeGuid = string.Empty;
    public string GlobalCoolTimeGuid {
        get {
            if (string.IsNullOrEmpty(_globalCoolTimeGuid)) {
                _globalCoolTimeGuid = $"{guid}global";
            }
            return _globalCoolTimeGuid;
        }
    }
    

    [ShowIf("@this.type.HasFlag(AttackTriggerType.AttackRate)")]
    public int attackCountRate = 0;

    [SlotName]
    public string slotName;
    public string triggerName;

    public bool IsValid => IsSkill || IsTrigger;

    public bool IsTrigger => !string.IsNullOrEmpty(triggerName);

    public bool IsSkill => !string.IsNullOrEmpty(slotName);

    public float minLimit;


    [SerializeField, HideInInspector]
    private CollisionInfo _collisionInfo = new CollisionInfo();

    [ShowIf(nameof(NeedLocalRange))]
    public CollisionInfo collisionInfo {
        get {
            _collisionInfo.collisionType = DamageInfo.CollisionType.Arc;
            _collisionInfo.near          = near;
            _collisionInfo.far           = far;
            _collisionInfo.angle         = angle;
            _collisionInfo.offset        = offset;
            return _collisionInfo;
        }
    }

    [ShowIf("NeedLocalRange")]
    public float near;

    [ShowIf("NeedLocalRange")]
    public float far;

    [ShowIf("NeedLocalRange")]
    public float angle;

    [ShowIf("NeedLocalRange")]
    public Vector3 offset;

    [Space]
    [DamageID]
    public int damageID = 0;
    public DamageInfo.CollisionType CollisionType => DamageData?.collisionInfo.collisionType ?? collisionInfo.collisionType;

    private DamageInfo _damageData;

    public DamageInfo DamageData {
        get {
#if UNITY_EDITOR && !ZIPDATA_EDITORTEST
            if (Application.isEditor && (_damageData == null || _damageData.damageID != damageID)) {
                _damageData = EditorGlobalData.damageInfos.Find(d => d.damageID == damageID);
            }
#endif
            if ((_damageData == null || _damageData.damageID != damageID) && DamageDataManager.instance != null && Application.isPlaying) {
                _damageData = DamageDataManager.instance.GetDamageDataByID(damageID);
            }
            return _damageData;
        }
    }


    public int MaxAttackCount = 0;
    private int triggerAttackCount = 0;

    private bool UseCoolItem => coolTime > 0;
    private Character character;

    public string imagePath;
#if UNITY_EDITOR
    [ShowInInspector, AssetSelector(FlattenTreeView = false, ExpandAllMenuItems = false)]
    public Sprite Icon {
        get => UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
        set {
            if (value == null) {
                imagePath = null;
                return;
            }

            imagePath = UnityEditor.AssetDatabase.GetAssetPath(value);
        }
    }
#endif
    [System.NonSerialized]
    private Sprite _image;
    public Sprite Image {
        get {
            if (string.IsNullOrEmpty(imagePath))
                return null;
            if (_image == null || Application.isPlaying == false)
                _image = UnityUtil.LoadSprite(imagePath);
            return _image;
        }
    }

    public void Init(Character parent) {
        character = parent;
        triggerAttackCount = 0;
        if (UseCoolItem) {
            if (GameManager.clientmode == GameManager.ClientMode.Game) {
                GameManager.ResetCooltime(guid);
                GameManager.ResetCooltime(GlobalCoolTimeGuid);
                if (type.HasFlag(AttackTriggerType.FirstAttackDelay)) {
                    SetCoolTime(guid, firstAttackCoolTime);
                }
            }
            
            if (GameManager.clientmode == GameManager.ClientMode.DedicateServer) {
                if(character.playController.gameRule is GameRuleDealDungeon gameRule) {
                    gameRule.playerData?.cooltimeManager.ResetCooltime(guid);
                    gameRule.playerData?.cooltimeManager.ResetCooltime(GlobalCoolTimeGuid);
                    var dedicateCoolTime = gameRule.playerData?.cooltimeManager.GetCoolTimeData(guid);
                    if(dedicateCoolTime != null) {
                        var notCoolTime = new CoolTime {
                            startTime = dedicateCoolTime.start,
                            duration = 0,
                            cooltime = 0,
                            guid = guid,
                        };
                        Starter.Servers.ChannelServer.Proxy.Not_MonsterCoolTime(Nettention.Proud.HostID.HostID_Server,
                            Nettention.Proud.RmiContext.ReliableSend, gameRule.playInstance.channeluid, gameRule.playerData.groupuid, notCoolTime);
                    }
                    if (type.HasFlag(AttackTriggerType.FirstAttackDelay)) {
                        SetCoolTime(guid, firstAttackCoolTime);
                    }
                }
            }
        }
    }
    
    public string UseAttackInfoAndGlobalCoolTime() {
        if (UseCoolItem) {
            SetCoolTime(guid, coolTime);
        }
        triggerAttackCount += 1;
        return OnGlobalCoolTime();
    }

    private void SetCoolTime(string _guid, float _coolTime) {
        if (GameManager.clientmode == GameManager.ClientMode.Game) {
            Starter.API.Game.GroupData.cooltimeManager.SetCoolTime(_guid, _coolTime, _coolTime);
        }
        
        if (GameManager.clientmode == GameManager.ClientMode.DedicateServer) {
            if(character.playController.gameRule is GameRuleDealDungeon gameRule) {
                gameRule.playerData?.cooltimeManager.SetCoolTime(guid, _coolTime, _coolTime);
                var dedicateCoolTime = gameRule.playerData?.cooltimeManager.GetCoolTimeData(guid);
                if(dedicateCoolTime != null) {
                    var notCoolTime = new CoolTime {
                        startTime = dedicateCoolTime.start,
                        duration = (float)dedicateCoolTime.origin.TotalSeconds,
                        cooltime = (float)dedicateCoolTime.origin.TotalSeconds,
                        guid = guid,
                    };
                    Starter.Servers.ChannelServer.Proxy.Not_MonsterCoolTime(Nettention.Proud.HostID.HostID_Server,
                        Nettention.Proud.RmiContext.ReliableSend, gameRule.playInstance.channeluid, gameRule.playerData.groupuid, notCoolTime);
                }
            }
        }
    }

    private bool CheckHP(Unit my) {
        if (type.HasFlag(AttackTriggerType.HPRate)) {
            var currentHpRate = my.status.CurrentHealthRatio;
            return must ? currentHpRate <= hpRateMin : currentHpRate <= hpRateMax && currentHpRate >= hpRateMin;
        }
        return true;
    }

    private bool CanUseAttackCount() {
        if (must && MaxAttackCount <= 0) {
            Debug.LogError("Must옵션을 사용할때는 꼭 MaxAttackCount를 0 이상 셋팅해야됩니다");
            MaxAttackCount = 1;
        }
        return MaxAttackCount == 0 || MaxAttackCount > triggerAttackCount;
    }

    private bool CheckCoolTime() {
        if (UseCoolItem == false) return true;
        if (GameManager.clientmode == GameManager.ClientMode.Game) {
            if (Starter.API.Game.GroupData.cooltimeManager.CheckCooltime(guid) == false) return false;
        }
        else {
            if(character.playController.gameRule is GameRuleDealDungeon gameRule) {
                if (gameRule.playerData?.cooltimeManager.CheckCooltime(guid) == false) return false;
            }
        }
        return true;
    }

    private bool CheckAttackRate(int attackCount) {
        if (type.HasFlag(AttackTriggerType.AttackRate)) {
            if (attackCount % attackCountRate != 0) return false;
        }
        return true;
    }

    private bool CheckRange(Character my, Unit target) {
        if (type.HasFlag(AttackTriggerType.Range)) {
            if (IsTargetInRangeBothSide(my, target) == false) return false;
        }
        return true;
    }
    
    public bool IsIgnoreRange() {
        return !type.HasFlag(AttackTriggerType.Range);
    }

    private bool CheckLevel(Character my) {
        if (type.HasFlag(AttackTriggerType.CharacterLevel)) {
            return my.level >= minLevel && my.level <= maxLevel;
        }
        return true;
    }

    public bool IsOnTrigger(Character my, Unit target, int attackCount) {
        if (CheckCoolTime() == false)              return false;
        if (CanUseAttackCount() == false)          return false;
        if (CheckHP(my) == false)                  return false;
        if (CheckAttackRate(attackCount) == false) return false;
        if (CheckRange(my, target) == false)       return false;
        if (CheckLevel(my) == false)               return false;
        return true;
    }

    public string OnGlobalCoolTime() {
        if (type.HasFlag(AttackTriggerType.GlobalCoolTime)) {
            SetCoolTime(GlobalCoolTimeGuid, globalCoolTime);
            return GlobalCoolTimeGuid;
        }

        return string.Empty;
    }

    public Range GetRangeMinMax(Transform transform) {
        var localScale = transform.localScale;
        if (NeedLocalRange()) {
            GetCollisionInfoRangeMinMax(collisionInfo, localScale, out var range);
            return range;
        }

        if (DamageData.damageType == DamageInfo.DamageType.Melee) {
            if (GetCollisionInfoRangeMinMax(DamageData.collisionInfo, localScale, out var rangeMinMax))
                return rangeMinMax;
        }
        else if (DamageData.damageType == DamageInfo.DamageType.Bullet) {
            var range = DamageData.damageTypeParam * DamageData.lifeTime;
            return new Range() { min = DamageData.startOffset.x, max = DamageData.startOffset.x + range * 0.7f };
        }

        return Range.One;
    }

    private static bool GetCollisionInfoRangeMinMax(CollisionInfo damageCollisionInfo, Vector3 localScale, out Range rangeMinMax) {
        switch (damageCollisionInfo.collisionType) {
            case DamageInfo.CollisionType.Arc:
                rangeMinMax = new Range {
                    max = (damageCollisionInfo.far - damageCollisionInfo.near + damageCollisionInfo.offset.x) * 0.75f * Mathf.Abs(localScale.x),
                    min = (damageCollisionInfo.near + damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                };
                break;
            case DamageInfo.CollisionType.Circle:
                rangeMinMax = new Range {
                    max = (damageCollisionInfo.radius * 0.5f - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                    min = (-damageCollisionInfo.radius * 0.5f - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                };
                break;
            case DamageInfo.CollisionType.Ellipse:
                rangeMinMax = new Range {
                    max = (damageCollisionInfo.ellipse_radius.x - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                    min = (-damageCollisionInfo.ellipse_radius.x - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                };
                break;
            case DamageInfo.CollisionType.Square:
                rangeMinMax = new Range {
                    max = (damageCollisionInfo.square_size.x * 0.5f - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                    min = (-damageCollisionInfo.square_size.x * 0.5f - damageCollisionInfo.offset.x) * Mathf.Abs(localScale.x),
                };
                break;
            default:
                rangeMinMax = Range.One;
                return false;
        }

        return true;
    }


    public bool NeedLocalRange() {
        if (damageID <= 0)
            return true;

        if (DamageData == null)
            return true;

        return DamageData.damageType != DamageInfo.DamageType.Melee && 
               DamageData.damageType != DamageInfo.DamageType.Bullet;
    }

    public bool IsTargetInRangeBothSide(Character parent, Unit target) {
        if (target == null) {
            return false;
        }

        if (DamageData != null && DamageData.collisionInfo.collisionType != DamageInfo.CollisionType.None) {
            var col = DamageData.collisionInfo;
            switch (DamageData.damageType) {
                case DamageInfo.DamageType.Melee:
                    return col.IsTargetInRange(parent.transform, target, Vector3.zero, false, DamageData.startOffset) 
                        || col.IsTargetInRange(parent.transform, target, Vector3.zero, true, DamageData.startOffset);
                case DamageInfo.DamageType.Bullet: {
                    var dir   = Vector2.left;
                    var range = DamageData.damageTypeParam * DamageData.lifeTime;
                    dir = Quaternion.Euler(DamageData.damageTypeVector) * dir * range;
                    var scale = parent.transform.localScale;
                    var isRight             = scale.x < 0;
                    if (isRight) {
                        dir.x *= -1;
                    }
                    
                    if (target.useCollider) {
                        var hits = Physics2D.RaycastAll(parent.transform.position, dir.normalized, range);
                        Debug.DrawRay(parent.position, dir.normalized * range, Color.green);
                        return hits?.Any(r => r.transform.gameObject.Equals(target.transform.gameObject)) ?? false;
                    }

                    var targetVector = (Vector2) (target.position - (parent.position + Vector3.Scale(col.offset + DamageData.startOffset, scale)));
                    var angle        = Vector2.Angle(targetVector, dir);
                    var radian       = Mathf.Deg2Rad * angle;
                    var length       = targetVector.magnitude;
                    var radius       = length * Mathf.Sin(radian); // 높이를 너무 타이트하지 않게 살짝 좁게 잡아줌.
                    var dist         = length * Mathf.Cos(radian);

                    const float yCalibration = 0.5f;
                    switch (col.collisionType) {
                        case DamageInfo.CollisionType.Circle: {
                            return radius < col.radius * yCalibration * scale.y
                                && radius > -col.radius * yCalibration * scale.y
                                && dist < range;
                        }
                        case DamageInfo.CollisionType.Arc: {
                            var height = Mathf.Sin(Mathf.Deg2Rad * col.angle) * col.far;
                            return radius < height * yCalibration * scale.y
                                && radius > -height * yCalibration * scale.y
                                && dist < range;
                        }
                        case DamageInfo.CollisionType.Ellipse: {
                            return radius < col.ellipse_radius.y * yCalibration * scale.y
                                && radius > -col.ellipse_radius.y * yCalibration * scale.y
                                && dist < range;
                        }
                        case DamageInfo.CollisionType.Square: {
                            return radius < col.square_size.y * 0.5f * yCalibration * scale.y
                                && radius > -col.square_size.y * 0.5f * yCalibration * scale.y
                                && dist < range;
                        }
                    }
                    return Mathf.Abs(radius) < col.radius && dist < range;
                }
            }
        }

        return collisionInfo.IsTargetInRange(parent.transform, target, Vector3.zero, false)
            || collisionInfo.IsTargetInRange(parent.transform, target, Vector3.zero, true);
    }

#if UNITY_EDITOR
    public void DrawGizmos(Transform transform) {
        if(NeedLocalRange())
            CollisionInfoExtension.DrawCollisionGizmos(transform, collisionInfo, Vector3.zero);
        else
            CollisionInfoExtension.DrawDamageDataRange(DamageData, transform);
    }
#endif
}


[Serializable]
public class CharacterAttackPattern {
    [InfoBox("[Type 옵션에 대한 설명]\n\n" +
             "Default: 기본 MeleeAttack 무조건 패턴에 하나이상 체크되어있어야됩니다.\n\n" +
             "Range: 범위 안에 있을때 조건 \n\n" +
             "HPRate: HP 비율 min max사이 조건 \n\n" +
             "HPRate + must옵션: HP 비율 min이하가 되면 우선적으로 공격 - MaxAttackCount 0 이상으로 설정하셔야됩니다. \n\n" +
             "AttackRate: 몇번째 공격 횟수마다 사용\n\n" +
             "GlobalCoolTime: 공격 후 딜레이(후딜) - 와우의 글로벌쿨타임\n\n" +
             "CharacterLevel: 사용자 Level minLevel 이상 ~ maxLevel이하\n\n" +
             "\n")]
    
    public List<CharacterAttackInfo>  attackInfos = new ();
    public bool                       useSkillinfoMeleeAttack;
    private Character                 _character;
    private string                    _globalCoolTimeGuid;
    private List<CharacterAttackInfo> meleeAttack;

    public void Initialize(Character parent) {
        _character = parent;
        foreach (var info in attackInfos) {
            info.Init(parent);
        }
        meleeAttack = attackInfos.Where(r => r.type.HasFlag(CharacterAttackInfo.AttackTriggerType.Default)).ToList();
    }

    public void UseAttackInfo(CharacterAttackInfo attackInfo) {
        _globalCoolTimeGuid = attackInfo.UseAttackInfoAndGlobalCoolTime();
    }

    private bool CheckGlobalCoolTime() {
        if (string.IsNullOrEmpty(_globalCoolTimeGuid)) return true;
        if (GameManager.clientmode == GameManager.ClientMode.DedicateServer) {
            if(_character.playController.gameRule is GameRuleDealDungeon gameRule) {
                return gameRule.playerData?.cooltimeManager.CheckCooltime(_globalCoolTimeGuid) ?? false;
            }
        }
        return GameManager.CheckCooltime(_globalCoolTimeGuid);
    }

    public CharacterAttackInfo GetNextAttackInfo() {
        if (_character == null) return null;
        if (CheckGlobalCoolTime() == false) return null;
        return useSkillinfoMeleeAttack ? GetUseSkillinfoMeleeAttack() : GetNextPattern();
    }

    private CharacterAttackInfo GetUseSkillinfoMeleeAttack() {
        if (meleeAttack.Count == 0) {
            Debug.LogError("패턴 타입에 Default 타입 패턴이 무조건 하나 추가가 되어있어야됩니다.");
            return null;
        }
        _character.currentAttackIndex %= meleeAttack.Count;
        return meleeAttack[_character.currentAttackIndex];
    }

    private CharacterAttackInfo GetNextPattern() {
        var patterns = attackInfos
                                            .Where(r => r.IsOnTrigger(_character, _character.attack_target, _character.Attack_target_count))
                                            .ToList();
        
        //must AttackInfoCheck
        if (patterns.Any(r => r.must)) {
            patterns = patterns
                .Where(r => r.must)
                .GroupBy(r => r.priority)
                .OrderByDescending(o => o.Key)
                .FirstOrDefault()?
                .ToList();
            return GetRandomListElement(patterns);
        }

        patterns = patterns
            .GroupBy(r => r.priority)
            .OrderByDescending(o => o.Key)
            .FirstOrDefault()?.ToList();
        if ((patterns?.Count ?? 0) > 0) {
            return GetRandomListElement(patterns);
        }
        
        //사용할 패턴이 없으면 Default스킬들 중 하나를 랜덤 사용
        if (meleeAttack.Count == 0) {
            Debug.LogError("패턴 타입에 Default 타입 패턴이 무조건 하나 추가가 되어있어야됩니다.");
            return null;
        }
        return GetRandomListElement(meleeAttack);
    }

    private CharacterAttackInfo GetRandomListElement(List<CharacterAttackInfo> infos) {
        if (infos == null || infos.Count <= 0) return null;
        var rand = infos.Count;
        rand = Random.Range(0, rand);
        return infos[rand];
    }
}
