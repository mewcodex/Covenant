"""Guard semantic boundaries of the staged proposal compiler."""
import importlib.util
import sys
import unittest
from pathlib import Path

module_path = Path(__file__).resolve().parents[1] / 'tools/compile_revision05.py'
sys.path.insert(0, str(module_path.parent))
spec = importlib.util.spec_from_file_location('revision05', module_path)
compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compiler)


class RevisionCompilerTest(unittest.TestCase):
    def rules(self, text, upgraded=False):
        return compiler.compile_program(compiler.expand(text, upgraded))['rules']

    def test_upgrading_changes_both_condition_and_grant(self):
        text = '打出时，若此牌当时的能量消耗不高于2（3），使已激活的【重装】增加8（11）层。'
        base, upgraded = self.rules(text)[0], self.rules(text, True)[0]
        self.assertEqual((base['condition'], base['amount']), ('before_cost<=2', '8*(1)'))
        self.assertEqual((upgraded['condition'], upgraded['amount']), ('before_cost<=3', '11*(1)'))

    def test_target_comes_from_revised_text(self):
        rule = self.rules('打出时，使已激活的【狂战】增加6层。')[0]
        self.assertEqual(rule['bond'], 'Fury')
        self.assertNotIn('requireActive', rule)  # The runtime default is true.
        self.assertEqual(rule['limit'], '')

    def test_explicit_first_play_limit_is_kept(self):
        rule = self.rules('每场战斗首次打出时，使已激活的【共鸣】增加4层。')[0]
        self.assertEqual(rule['limit'], 'battle')

    def test_only_native_power_support_is_temporary(self):
        program = compiler.compile_program('打出后，每当它使你抽牌，使已激活的【幽逝】临时增加2层。')
        self.assertEqual(program['rules'][0]['action'], 'temporary')
        self.assertEqual(program['rules'][0]['event'], 'native')
        self.assertEqual(program['rules'][0]['condition'], 'drawn_count>0')

    def test_pre_existing_debuff_does_not_include_own_application(self):
        rule = self.rules('打出时，若目标在此之前已有中毒，使已激活的【毒棘】增加6层。')[0]
        self.assertEqual(rule['condition'], 'before_target_PoisonPower>0')

    def test_own_stat_without_before_wording_includes_native_effect(self):
        rule = self.rules('打出时，你每有1点敏捷，使已激活的【坚守】增加2层。')[0]
        self.assertEqual(rule['amount'], '2*(after_owner_DexterityPower)')

    def test_independent_extra_effect_is_not_activation_gated(self):
        rules = self.rules('抽到这张牌时，使已激活的【残响】增加2层，并获得1点格挡。')
        self.assertEqual(len(rules), 2)
        self.assertEqual(rules[1]['action'], 'block')
        self.assertFalse(rules[1]['requireActive'])

    def test_paired_growth_keeps_each_target(self):
        rules = self.rules('打出时，使已激活的【狂战】增加4层，并使已激活的【坚守】增加2层。')
        self.assertEqual([rule['bond'] for rule in rules], ['Fury', 'Guard'])

    def test_unknown_clauses_cannot_be_dropped(self):
        for text in [
            '打出时，使已激活的【坚守】增加2层，并使下一张牌免费。',
            '打出时，若某个未知条件成立，使已激活的【坚守】增加2层。',
            '打出时，使已激活的【坚守】增加2层；否则增加1层。',
            '获得此牌时，使【先古】增加12层。',
        ]:
            with self.subTest(text=text), self.assertRaises(compiler.Pending):
                self.rules(text)

    def test_generated_card_origin_uses_its_own_printed_cost(self):
        rule = self.rules('由此获得的攻击牌被打出时，使已激活的【造物】增加层数，数量为其原始费用加1的2倍。')[0]
        self.assertEqual((rule['event'], rule['scope'], rule['parameter']), ('subject_play', 'generated', 'Attack'))
        self.assertEqual(rule['amount'], '2*(subject_cost+1)')
        self.assertEqual(rule['lifetime'], 'battle')

    def test_same_turn_restriction_is_not_a_shared_hard_cap(self):
        rule = self.rules('打出后，每当由它生成的攻击牌在同一回合被打出，使已激活的【造物】临时增加4层。')[0]
        self.assertEqual(rule['lifetime'], 'turn')
        self.assertEqual(rule['action'], 'temporary')
        self.assertNotIn('limit', rule)

    def test_passive_bonus_has_an_independent_resolved_display(self):
        program = compiler.compile_program('此牌每次额外造成2点伤害；【独行】每3档再增加1点。')
        self.assertEqual(program['rules'][0]['amount'], '2+floor(A/3)*1')
        self.assertEqual(program['amountText'], '此牌每次额外造成{0}点伤害。')
        self.assertFalse(program['rules'][0]['requireActive'])

    def test_conditional_passive_keeps_both_parts(self):
        program=compiler.compile_program('此牌每次额外造成1点伤害；【独行】每4档再增加1点。命中具有易伤的敌人时再增加1点。','Conflagration')
        self.assertEqual(len(program['rules']),2)
        self.assertEqual(program['rules'][1]['condition'],'after_target_VulnerablePower>0')
        self.assertEqual(program['amountText'],'此牌每次额外造成{0}点伤害。命中具有易伤的敌人时再增加1点。')

    def test_first_generated_play_expires_per_physical_subject(self):
        program=compiler.compile_program('打出时，使由此生成的每张小刀下次打出时额外使已激活的【飞刃】增加2层。','BladeDance')
        rule=program['rules'][0]
        self.assertEqual((rule['scope'],rule['lifetime'],rule['parameter']),('generated','next','class_Shiv'))

    def test_top_card_support_has_no_activation_requirement(self):
        program=compiler.compile_program('打出时，使被放回抽牌堆顶的牌下次打出时造成的伤害和获得的格挡增加3点。','Headbutt')
        self.assertEqual([r['event'] for r in program['rules']],['damage_bonus','block_bonus'])
        self.assertTrue(all(r['scope']=='top' and r['lifetime']=='next' and not r['requireActive'] for r in program['rules']))

    def test_delayed_keyword_retains_its_turn_condition(self):
        rule=compiler.compile_program('被它添加保留的那张牌在之后的回合被打出时，使已激活的【延续】增加6层。','Snap')['rules'][0]
        self.assertEqual((rule['scope'],rule['condition']),('keyword_Retain','subject_before_turn>attachment_turn'))

    def test_native_poison_and_death_triggers_preserve_multipliers(self):
        fumes=compiler.compile_program('打出后，每当它使一名敌人的中毒达到10层的倍数，使已激活的【毒棘】临时增加4层。','NoxiousFumes')['rules'][0]
        self.assertEqual(fumes['amount'],'4*(poison_crossed_10)')
        compost=compiler.compile_program('打出后，每当它因敌人死亡而提供收益，使已激活的【零界】增加4层。','Compost')['rules'][0]
        self.assertIn('native_method_AfterDeath',compost['condition'])

    def test_entropy_first_transform_has_two_independent_effects(self):
        program=compiler.compile_program('打出后，每回合首次变化卡牌时，获得3点格挡，并使变化后的牌在本回合的能量消耗减少1；【调和】每1档使格挡再增加1点。','Entropy')
        self.assertEqual([r['action'] for r in program['rules']],['block','discount_subject'])
        self.assertTrue(all(r['limit']=='turn' and not r['requireActive'] for r in program['rules']))

    def test_replay_origins_do_not_share_a_battle_limit(self):
        first=compiler.compile_program('被它添加重放的那张牌首次重放时，使已激活的【独行】增加6层。','HiddenGem')['rules'][0]
        repeated=compiler.compile_program('每场战斗中，被它添加重放的那张牌每次重放时，使已激活的【余烬】增加2层。','Transfigure')['rules'][0]
        self.assertEqual((first['scope'],first['lifetime']),('base_replay','next_replay'))
        self.assertEqual((repeated['scope'],repeated['lifetime']),('base_replay','battle'))
        self.assertNotIn('limit',repeated)

    def test_skill_support_keeps_its_exclusive_branches(self):
        rules=compiler.compile_program('打出时，使被取回的技能牌在本回合获得的格挡增加4点，或使其叠层额外增加4层。','SecretTechnique')['rules']
        self.assertEqual([r['condition'] for r in rules],['subject_gains_block>0','subject_gains_block==0'])
        self.assertEqual([r['event'] for r in rules],['block_bonus','growth_bonus'])

    def test_acquisition_exception_does_not_ungate_generated_power_growth(self):
        rules=compiler.compile_program('获得此牌时，使【先古】增加12层；由它加入手牌的能力牌被打出时，使已激活的【仪式】增加6层。','Abundance')['rules']
        self.assertFalse(rules[0]['requireActive'])
        self.assertNotIn('requireActive',rules[1])

    def test_last_allowed_play_checks_the_real_remaining_counter(self):
        rule=compiler.compile_program('打出后的本回合，你打出的最后一张被允许的牌使已激活的【零界】增加层数，数量为其原始费用加1的2倍。','Encore')['rules'][0]
        self.assertEqual(rule['condition'],'native_method_AfterCardPlayed>0&&before_power_var_PlaysRemaining==1&&native_power_var_PlaysRemaining==0')


if __name__ == '__main__':
    unittest.main()
