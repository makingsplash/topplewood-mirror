import jenkins
from string import Template


def start_job(job_name, parameters=None):
	if parameters is None:
		parameters = {}

	server = jenkins.Jenkins('http://macmini.internal.saygames.io:8080', username='***REMOVED***', password='***REMOVED***')
	server.build_job("team-game-pie-vanila/" + job_name + "_" + parameters["platform"], parameters)


def create_job(git_url):
	print(git_url)
	if (not git_url.startswith("<http")) or (not git_url.endswith(".git>")):
		raise Exception("Invalid git url. Should start with http/https and end with .git. Given: " + str(git_url))

	project_name = git_url.split("/")[-1][:-5]

	server = jenkins.Jenkins('http://macmini.internal.saygames.io:8080', username='***REMOVED***', password='***REMOVED***')
	conf_t = ""
	with open("./job_template.jenkins", "r") as f:
		conf_t = f.read()

	if not conf_t:
		raise Exception("can't read config template")

	conf_t = Template(conf_t)
	conf_res = conf_t.safe_substitute(_git_url_=git_url[1:-1], _project_name_=project_name)

	server.create_job("team-game-pie-vanila/" + project_name + "_android", conf_res)
	server.create_job("team-game-pie-vanila/" + project_name + "_ios", conf_res)
	return project_name
