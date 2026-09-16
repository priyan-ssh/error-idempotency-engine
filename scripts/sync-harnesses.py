import os
import shutil

# Derive repo root from script location — works on any machine, any checkout path
script_dir = os.path.dirname(os.path.abspath(__file__))
repo_root = os.path.dirname(script_dir)

core_dir = os.path.join(repo_root, "core")
agy_dir = os.path.join(repo_root, ".agents")


def sync_dir(src, dst):
    if os.path.exists(dst):
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    print(f"  synced  {os.path.relpath(src, repo_root)}  →  {os.path.relpath(dst, repo_root)}")


print("Syncing core/ → .agents/ ...")

# Skills (identical format — no transformation needed)
sync_dir(os.path.join(core_dir, "skills"), os.path.join(agy_dir, "skills"))

# Rules
sync_dir(os.path.join(core_dir, "rules"), os.path.join(agy_dir, "rules"))

# Plugins
plugins_src = os.path.join(core_dir, "plugins")
if os.path.exists(plugins_src):
    sync_dir(plugins_src, os.path.join(agy_dir, "plugins"))
    # Wire skills and agents into plugins so agy plugin recognizes them
    plugin_stack = os.path.join(agy_dir, "plugins", "idempotency-engine-stack")
    if os.path.exists(plugin_stack):
        p_agents = os.path.join(plugin_stack, "agents")
        p_skills = os.path.join(plugin_stack, "skills")
        if not os.path.exists(p_agents):
            os.symlink("../../agents", p_agents)
        if not os.path.exists(p_skills):
            os.symlink("../../skills", p_skills)

# Agents
core_agents = os.path.join(core_dir, "agents")
if os.path.exists(core_agents):
    agents_dst_dir = os.path.join(agy_dir, "agents")
    sync_dir(core_agents, agents_dst_dir)
    for item in os.listdir(core_agents):
        src = os.path.join(core_agents, item)
        if os.path.isdir(src):
            agent_md = os.path.join(src, "agent.md")
            if os.path.exists(agent_md):
                flat_dst = os.path.join(agents_dst_dir, f"{item}.md")
                shutil.copy2(agent_md, flat_dst)
            legacy_dst = os.path.join(agy_dir, item)
            if os.path.exists(legacy_dst):
                shutil.rmtree(legacy_dst)
            shutil.copytree(src, legacy_dst)
            print(f"  synced  agents/{item}")

print("\nDone. .agents/ is up to date.")
