import jenkins
import sys


if __name__ == "__main__":
	server = jenkins.Jenkins('http://macmini.internal.saygames.io:8080', username='***REMOVED***', password='***REMOVED***')

	job_name = sys.argv[1]
	file_path = sys.argv[2]

	config = server.get_job_config(job_name)
	with open(file_path, "w") as f:
		f.write(config)
