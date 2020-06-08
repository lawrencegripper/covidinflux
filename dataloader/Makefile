build:
	dotnet build

clean:
	dotnet clean

build-docker:
	rm -f tag.txt 
	date +%s > tag.txt
	docker build -t gripdev.azurecr.io/pheinfluxloader:$$(cat tag.txt) .

push: build-docker
	az acr login -n gripdev
	docker push gripdev.azurecr.io/pheinfluxloader:$$(cat tag.txt)

run-docker: build-docker
	docker run -t gripdev.azurecr.io/pheinfluxloader:$$(cat tag.txt)
