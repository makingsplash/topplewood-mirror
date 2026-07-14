import os
import sys
from string import Template


if __name__ == "__main__":
	print("add_builder_script")
	print(sys.argv)
	print(sys.argv[4])
	unity_project_name = sys.argv[1]
	unity_project_path = sys.argv[2]
	#abtest_version = sys.argv[3] if len(sys.argv) > 3 else ""
	unity_china_version = sys.argv[3]
	unity_bot_version = sys.argv[4];

	with open(unity_project_path + "/buildscripts/Builder.cs", "r") as template_file:
		template_text = Template(template_file.read())
		res = template_text.substitute(project_name=unity_project_name, china_version=unity_china_version, bot_version = unity_bot_version)

		directory = unity_project_path + "/Assets/Editor"
		os.makedirs(directory, exist_ok=True)
		with open(directory + "/Builder.cs", "w") as f:
			f.write(res);
