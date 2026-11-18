pipeline {
    agent any

    options {
        skipDefaultCheckout(true)
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                bat 'git log -1 --format="Commit: %%H %%s"'
            }
        }

        stage('Restore') {
            steps {
                bat 'dotnet restore DistributedSystem.slnx'
            }
        }

        stage('Build') {
            steps {
                bat 'dotnet build DistributedSystem.slnx --configuration Release --no-restore'
            }
        }

        stage('Test') {
            steps {
                bat 'dotnet test Tests/Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Tests.Unit" --logger "trx;LogFileName=unit.trx" --results-directory TestResults'
                bat 'dotnet test Tests/Tests.csproj --configuration Release --no-build --filter "Category=Integration" --logger "trx;LogFileName=integration.trx" --results-directory TestResults'
                bat 'dotnet test Tests/Tests.csproj --configuration Release --no-build --filter "Category=Kubernetes" --logger "trx;LogFileName=kubernetes.trx" --results-directory TestResults'
                bat 'dotnet test Tests/Tests.csproj --configuration Release --no-build --filter "Category=Load" --logger "trx;LogFileName=load.trx" --results-directory TestResults'
            }
            post {
                always {
                    archiveArtifacts artifacts: 'TestResults/*.trx', allowEmptyArchive: true
                }
            }
        }

        stage('Package') {
            steps {
                bat 'docker version'
                bat 'docker build -f Server/Dockerfile -t education-server:%BUILD_NUMBER% .'
                bat 'docker image inspect education-server:%BUILD_NUMBER% --format "Image: {{index .RepoTags 0}}  Id: {{.Id}}  Created: {{.Created}}  Size: {{.Size}}"'
            }
        }
    }
}