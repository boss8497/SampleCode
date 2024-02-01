using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

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
