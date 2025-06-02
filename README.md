**WebCrawler Solution**

**Overview**
This solution is a distributed web crawling and data aggregation system built with .NET 9. It leverages multiple Docker containers to parallelize crawling tasks, combines and merges results, and stores the final data in Elasticsearch. The system is designed for scalability and efficient data processing, making it suitable for large-scale web data collection and analysis.
Architecture

•	**Crawlers** (crawler1–crawler10): Each crawler container processes a subset of domains and writes results to a shared volume.

•	**Combiner**: Waits for all crawlers to finish, then combines and merges their results.

•	**Elasticsearch**: Stores the merged results for querying and analysis.

•	**Web API** (webcrawler): Provides an interface for interacting with the system and querying results.


**Prerequisites**
•	Docker and Docker Compose installed.
•	.NET 9 SDK (for local development or building images).
•	The webcrawler-net Docker network must exist, or you can let Docker Compose create it.

**Docker Network Setup**
If you want Docker Compose to use an existing network, create it first: **docker network create webcrawler-net**
If you want Docker Compose to create the network automatically, remove the external: true line from the networks section in docker-compose.yml.

**Running the Solution**

1.	**Build and Start All Services**
From the WebCrawler directory (where docker-compose.yml is located):    **docker-compose up --build**

This will:

•	Build the crawler, combiner, and web API images.

•	Start 10 crawler containers, the combiner, Elasticsearch, and the web API.

•	All containers will communicate over the webcrawler-net network.

2.	**Stopping the Solution**
To stop all running containers:    **docker-compose down**

**Usage**
•	Crawling: Each crawler container will process its assigned domains and write a .done file when finished.

•	Combining/Merging: The combiner waits for all .done files, then merges results and writes to Elasticsearch.

•	Web API: Once running, the API is available at http://localhost:5000 (or as configured in docker-compose.yml).

Environment Variables

•	DOMAIN_FILE: CSV file with domains to crawl (set per crawler).

•	CRAWLER_DONE_FILE: Name of the .done file to signal completion.

•	RESULTS_DIRECTORY: Directory for results (shared via Docker volume).

•	ELASTICSEARCH_URI: URI for Elasticsearch (used by the web API).

**Example Workflow**

1.	Place your domains_*.csv files in the appropriate location.
2.	Run docker-compose up --build.
3.	Wait for all crawlers and the combiner to finish.
4.	Access the merged results via Elasticsearch or the web API.


**Troubleshooting**
•	Ensure the webcrawler-net network exists if using external: true.
•	Check logs with docker-compose logs for troubleshooting.
---
Summary of Docker Commands:
# (Optional) Create the network if using external: true
docker network create webcrawler-net

# Build and run all services
docker-compose up --build
or
docker-compose -f docker-compose.crawlers.yml -f docker-compose.elasticsearch.yml -f docker-compose.web.yml build --no-cache
docker-compose -f docker-compose.crawlers.yml -f docker-compose.elasticsearch.yml -f docker-compose.web.yml up -d

# Stop all services
docker-compose down



