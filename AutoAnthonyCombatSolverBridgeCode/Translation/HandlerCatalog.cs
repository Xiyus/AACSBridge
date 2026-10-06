namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 翻译表：把支持的 (Opcode, Variant) 形状登记进注册表。
/// 支持矩阵与 AutoAnthony 组件目录（catalog_runtime_specs.json，931 条）逐形状核对过：
///  - 0.1.0：deal_damage(selected/all 163) + gain_block(78) + draw_cards(50) + gain_energy(30)
///    + lose_hp(10) + heal(1)，全部 fixed 值；
///  - 0.2.0：apply_power 的 20 个 variant（76 条：vulnerable 18 / weak 17 / strength 9 /
///    strength_loss_this_turn 7 / dexterity_gain 4 / retain_hand_this_turn 3 / plating 3 /
///    vigor 3 / strength_loss 2 / strength_this_turn 2 / 其余各 1）+ Power 卡 Unpowered
///    伤害路径 + M:base/strength_scaled 格挡修饰符数学。
/// 其余形状（X 值源、随机目标、事件目标、历史计数、阈值翻倍、绑定 Power 等）由镜像层
/// 的校验 fail-closed 拒绝，等后续里程碑。
/// </summary>
public static class HandlerCatalog
{
    public static void RegisterAll(OperationHandlerRegistry registry)
    {
        var damage = new Handlers.DamageHandler();
        registry.Register(new OperationKey("deal_damage", "selected"), damage);
        registry.Register(new OperationKey("deal_damage", "all"), damage);
        registry.Register(new OperationKey("deal_damage", "random"), damage);
        registry.Register(new OperationKey("gain_block", "immediate"), new Handlers.BlockHandler());
        registry.Register(new OperationKey("draw_cards", "immediate"), new Handlers.DrawHandler());
        registry.Register(new OperationKey("gain_energy", "immediate"), new Handlers.EnergyHandler());
        registry.Register(new OperationKey("lose_hp", "immediate"), new Handlers.LoseHpHandler());
        registry.Register(new OperationKey("heal", "immediate"), new Handlers.HealHandler());

        var power = new Handlers.PowerHandler();
        foreach (var variant in new[]
                 {
                     "vulnerable", "weak", "strength_loss", "strength_loss_this_turn", "strength_gain",
                     "vulnerable_double", "retain_hand_this_turn", "strength", "strength_per_target_vulnerable",
                     "dexterity_gain", "dexterity_loss", "dexterity_gain_this_turn", "doom", "focus_loss",
                     "thorns", "intangible", "blur", "plating", "strength_this_turn", "vigor",
                     "poison",
                 })
            registry.Register(new OperationKey("apply_power", variant), power);

        // 0.3.0：牌堆移动（确定性形状——选牌/RNG 形状仍由校验层拒绝）
        registry.Register(new OperationKey("exhaust_card", "all"), new Handlers.ExhaustHandler());
        registry.Register(new OperationKey("discard_card", "all"), new Handlers.DiscardHandler());
        registry.Register(new OperationKey("create_copy", "this_card"), new Handlers.CreateCopyHandler());
        registry.Register(new OperationKey("draw_and_discard", "nonzero_cost"), new Handlers.DrawAndDiscardHandler());

        // 0.6.x：代理模板（解决 Imbued 附魔自动施放矩阵外卡导致的 TURN_SETUP_FAILURE）
        registry.Register(new OperationKey("template_independent_action", "cl_proxyatomic_hiddengem"),
            new Handlers.HiddenGemHandler());

        // 0.7.0：随机生成（分支 RNG + 递归镜像覆盖）
        var createCard = new Handlers.CreateCardHandler();
        registry.Register(new OperationKey("create_card", "random_colorless"), createCard);
        registry.Register(new OperationKey("create_card", "current_character_random"), createCard);
        registry.Register(new OperationKey("create_card", "random_zero_cost"), createCard);
        registry.Register(new OperationKey("create_card", "random_attack_zero_cost_this_turn"), createCard);

        // 0.8.0：目录审计补遗——gain_stars 是唯一"简单但未做"的即时效果（13 条）
        registry.Register(new OperationKey("gain_stars", "immediate"), new Handlers.GainStarsHandler());

        // 0.8.x：template_self_action 高频子族（球引导/聚焦/球位/Shiv/锻造/全体毒，41 条目录形状）
        var templateSelf = new Handlers.TemplateSelfActionHandler();
        foreach (var variant in new[]
                 {
                     "d_channelfrost", "d_channeldark", "d_channellightning",
                     "d_channelglass", "d_channelplasma", "d_channelrandom",
                     "d_gainfocus", "d_gaintemporaryfocus", "d_gainorbslots", "n_createshiv",
                     "r_forge", "n_allpoison", "d_evokerightmostorb", "d_loseorbslots",
                     "d_nextturnenergy", "d_losefocus", "d_gainstrength", "d_gaindexterity",
                     "d_triggerrightmostorbpassive", "ncr_applydoomall", "ncr_applyselfdoom",
                     "ncr_applyweakall", "ncr_applyvulnerableall", "r_enemieslosestrengththisturn",
                     // 单行 Power 模板族
                     "cl_retainhandthisturn", "r_retainhandthisturn", "cl_gaingold", "cl_noblockfromcards",
                     "cl_gainvigor", "r_gainvigor", "cl_gainnextturnblockequalcurrent",
                     "cl_applyweakall", "r_applyweakall", "n_allweak",
                     "cl_applyvulnerableall", "r_applyvulnerableall",
                     "r_gainstrengththisturn", "r_reflectblockeddamagethisturn", "r_gainstrength",
                     "r_enemieslosestrength", "r_kingsswordhitsallenemies",
                     "ncr_nextturnenergy", "ncr_losestrength", "ncr_nextvoidcostszero",
                     "d_nextpowercostszero",
                     // N: 族简单 Power + 状态牌创建
                     "n_thorns", "n_intangible", "n_tempdex", "n_nextturndraw",
                     "n_keepblocknextturn", "n_nextturnblock",
                     "d_createdazedindiscard", "d_createtwowoundsindiscard",
                     "d_createburnindiscard", "d_createslimeindiscard", "d_createvoidindiscard",
                     // 更多球激发/简单变体
                     "d_evokeleftmostorb", "d_evokealltwice", "n_createinkshiv",
                     "n_blockequalallpoison", "d_exhaustallstatuses", "d_shuffleallunexhaustedintodraw",
                     // 卡牌创建/生成/返回
                     "r_putkingsswordinhand", "d_addrandompowertohand",
                     "cl_addrandomattacktohand", "d_returnzerocostdiscardtohand",
                     // 更多卡牌创建
                     "r_adddebristohand", "d_createzerocostcopyindiscard",
                     "ncr_createsoulindiscard", "ncr_createsoulindraw", "ncr_createsoulinhand",
                 })
            registry.Register(new OperationKey("template_self_action", variant), templateSelf);

        // 0.9.x：combat_rule 代理 Power 模板（A:ProxyAtomic 族，5 条）
        foreach (var variant in new[]
                 {
                     "a_proxyatomic_buffer", "a_proxyatomic_parry", "a_proxyatomic_royalties",
                     "a_proxyatomic_calcify", "a_proxyatomic_swordsage", "a_proxyatomic_forbiddengrimoire",
                     "retain_hand_at_turn_end", "retain_block_between_turns",
                     "kings_sword_hits_all", "skills_cost_zero",
                 })
            registry.Register(new OperationKey("combat_rule", variant), templateSelf);

        // 0.8.x：template_target_action 目标模板（毒/X 力量损失/X 虚弱/末日，11 条）
        var templateTarget = new Handlers.TemplateTargetActionHandler();
        foreach (var variant in new[] { "t_poison", "t_xstrengthloss", "t_xweak", "ncr_applydoom",
                                          "t_removeblockandartifact", "ncr_targetlosestrength", "ncr_doublevulnerableweak" })
            registry.Register(new OperationKey("template_target_action", variant), templateTarget);

        // 0.8.x：template_independent_action 简单独立模板（禁抽/临时力量/全体易伤/最大生命/本卡成长，8 条）
        var templateIndependent = new Handlers.TemplateIndependentActionHandler();
        foreach (var variant in new[]
                 {
                     "i_preventdrawthisturn", "i_gaintemporarystrength",
                     "i_applytoallenemies", "i_gainmaxhp",
                     "i_increasedamagethiscombat", "d_increasethiscarddamagerun", "d_increasethiscardblockrun",
                     "i_doubleblockthisturn", "i_doubleattackdamagenextturn",
                     "i_freehandthisturn", "i_drawwithretain", "i_triggerpoisonnow",
                     "i_replaynextskills", "i_discardhanddrawsame", "cl_drawtofullhand",
                     "i_nextskillcostszero", "i_setthiscardcostzero",
                     "i_upgrade", "i_playtopcardandexhaust", "i_playthiscard",
                     "cl_exhaustuptohandcards", "d_increasethiscardcost",
                     // 代理模板（简单 Power/球操作）
                     "i_proxyatomic_foregoneconclusion", "i_proxyatomic_multicast", "i_proxyatomic_tempest",
                     "i_proxyatomic_whitenoise",
                 })
            registry.Register(new OperationKey("template_independent_action", variant), templateIndependent);
    }
}
